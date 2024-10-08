using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBotCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ReplicatorBot
{
	public class Replicator : BackgroundService
	{
		public static Replicator? Default { get; private set; }

		protected DiscordSocketClient Client { get; init; }
		protected InteractionService InteractionService { get; init; }
		protected ILogger<Replicator> Logger { get; init; }
		protected IConfiguration Configuration { get; init; }
		protected IServiceProvider Services { get; init; }
		private readonly List<Thread> _activeThreads = new();

		private readonly Random _random = new Random();

		public Replicator(
			DiscordSocketClient client,
			InteractionService interactionService,
			ILogger<Replicator> logger,
			IConfiguration config,
			IServiceProvider services)
		{
			Client = client;
			InteractionService = interactionService;
			Logger = logger;
			Configuration = config;
			Services = services;

			Default ??= this;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			Client.Log += LogAsync;
			Client.Ready += ReadyAsync;

			Client.GuildAvailable += GuildAvailableAsync;
			Client.GuildUnavailable += GuildUnavailableAsync;
			Client.JoinedGuild += JoinedGuildAsync;
			Client.LeftGuild += LeftGuildAsync;
			Client.MessageReceived += MessageReceievedAsync;

			stoppingToken.Register(UnregisterCallbacks);
			await Task.Delay(Timeout.Infinite, stoppingToken);
		}

		private async Task ReadyAsync()
		{
			await InteractionService.RegisterCommandsAsync(Services);
			InteractionService.InstallService(Services);
		}

		private void UnregisterCallbacks()
		{
			Client.GuildAvailable -= GuildAvailableAsync;
			Client.GuildUnavailable -= GuildUnavailableAsync;
			Client.JoinedGuild -= JoinedGuildAsync;
			Client.LeftGuild -= LeftGuildAsync;
			Client.MessageReceived -= MessageReceievedAsync;
			Client.Log -= LogAsync;
			Logger.LogInformation("Shutting down Replicator");
		}

		private async Task MessageReceievedAsync(SocketMessage message)
		{
			try
			{
				if ((message.Author as IGuildUser)?.Guild is not SocketGuild guild)
					return;

				using IServiceScope scope = Services.CreateScope();
				using ReplicatorContext context = scope.ServiceProvider.GetRequiredService<ReplicatorContext>();

				GuildConfig config = context.GuildConfig.AsQueryable().Include(g => g.ChannelPermissions).First(g => g.GuildId == guild.Id);
				ChannelPermissions permissions = config.ChannelPermissions.FirstOrDefault(c => c.GuildId == guild.Id && c.ChannelId == message.Channel.Id)
					?? ChannelPermissions.CreateDefault(context, guild.Id, message.Channel.Id);         // If permissions do not exist, create default permissions.

				Discord.ChannelPermissions channelPerms = guild.GetUser(Client.CurrentUser.Id).GetPermissions(message.Channel as IGuildChannel);

				if (config.Enabled)
				{
					config.LastUpdate = DateTime.UtcNow;
					if (channelPerms.ViewChannel)
					{
						if (!message.Author.IsBot && channelPerms.ReadMessageHistory && permissions.Permissions.HasFlag(ChannelPermission.Read))
						{
							config.GuildMessageCount += 1;
							if (TestMessageStorable(message, config))
								AddNewMessage(context, Client, message, config);
						}

						if (channelPerms.SendMessages && permissions.Permissions.HasFlag(ChannelPermission.Write) && !config.DisabledUsers.Select(d => d.UserId).Contains(message.Author.Id))
						{
							if (message.MentionedUsers.Select(u => u.Id).Contains(Client.CurrentUser.Id))
								await SendRandomMessageAsync(message.Channel, config).ConfigureAwait(false);

							if (_random.NextDouble() < config.Probability)
								await SendRandomMessageAsync(message.Channel, config).ConfigureAwait(false);
						}
					}

					context.GuildConfig.Update(config);
				}

				context.SaveChanges();
			}
			catch (Exception e)
			{
				using var typing = message.Channel.EnterTypingState();

				var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
				DateTime messageTime = TimeZoneInfo.ConvertTimeFromUtc(message.Timestamp.UtcDateTime, timeZone);

				await message.Channel.SendMessageAsync($"An error has occurred in response to message ({message.Id}) at {messageTime:G} {timeZone.StandardName} ({message.Content}):\n{e}", allowedMentions: AllowedMentions.None);
			}
		}

		private static void AddNewMessage(ReplicatorContext context, DiscordSocketClient client, IMessage message, GuildConfig config, bool save = true)
		{
			MessageType messageType = MessageType.Raw;
			string content;
			if (message.Stickers.Count != 0)
			{
				messageType = MessageType.Sticker;
				content = JsonSerializer.Serialize(message.Stickers.Select(s => s.Id).ToArray());
			}
			else
			{
				StringBuilder sb = new(message.Content);
				if (message.Attachments.Count != 0)
				{
					sb.Append(' ').AppendJoin(' ', message.Attachments.Where(a => !string.IsNullOrEmpty(a.Url)).Select(m => m.Url));
				}

				if (message.Embeds.Count != 0)
				{
					sb.Append(' ').AppendJoin(' ', message.Embeds.Where(a => !string.IsNullOrEmpty(a.Url)).Select(m => m.Url));
				}

				content = sb.ToString();
			}

			if (string.IsNullOrEmpty(content))
				return;

			Message m = new Message(config.GuildId, message.Id, messageType, config.TargetMessageCount, content);
			context.Messages.Add(m);

			config.TargetMessageCount += 1;
			if (config.AutoUpdateProbability)
				config.Probability = (double)config.TargetMessageCount / config.GuildMessageCount;

			context.GuildConfig.Update(config);

			if (save)
				context.SaveChanges();
		}

		private static Message AddNewMessageConcurrent(IMessage message, GuildConfig config)
		{
			MessageType messageType = MessageType.Raw;
			string content;
			if (message.Stickers.Count != 0)
			{
				messageType = MessageType.Sticker;
				content = JsonSerializer.Serialize(message.Stickers.Select(s => s.Id).ToArray());
			}
			else
			{
				StringBuilder sb = new(message.Content);
				if (message.Attachments.Count != 0)
				{
					sb.AppendJoin(' ', message.Attachments.Where(a => !string.IsNullOrEmpty(a.Url) && !message.Content.Contains(a.Url)).Select(m => m.Url)).Append('\n');
				}

				if (message.Embeds.Count != 0)
				{
					sb.AppendJoin(' ', message.Embeds.Where(a => !string.IsNullOrEmpty(a.Url) && !message.Content.Contains(a.Url)).Select(m => m.Url)).Append('\n');
				}

				content = sb.ToString();
			}

			return new Message(config.GuildId, message.Id, messageType, config.TargetMessageCount, content);
		}

		private async Task<bool> HandleStickers(ISocketMessageChannel channel, ulong[] stickerIds, AllowedMentions allowedMention)
		{
			List<FileAttachment> attachments = [];
			List<ISticker> usableStickers = [];
			using HttpClient client = new();

			foreach (var stickerId in stickerIds)
			{
				ISticker sticker = await Client.GetStickerAsync(stickerId);
				if (sticker is null)
					continue;

				if (sticker.Type == StickerType.Guild)
					attachments.Add(await StickerToAttachmentAsync(sticker, client));
				else
					usableStickers.Add(sticker);
			}

			if (usableStickers.Count == 0 && attachments.Count == 0)
				return false;

			if (usableStickers.Count > 0)
				await channel.SendMessageAsync(stickers: [.. usableStickers]);
			if (attachments.Count > 0)
			{
				await channel.SendFilesAsync(attachments);
				attachments.ForEach(attachments => attachments.Dispose());
			}

			return true;
		}

		public static async Task<FileAttachment> StickerToAttachmentAsync(ISticker sticker, HttpClient client)
		{
			ArgumentNullException.ThrowIfNull(sticker, nameof(sticker));

			using var response = await client.GetAsync($"https://cdn.discordapp.com/stickers/{sticker.Id}");

			MemoryStream ms = new();
			await response.Content.CopyToAsync(ms);

			string name = sticker.Format switch
			{
				StickerFormatType.Png or StickerFormatType.Apng => $"{sticker.Name}.png",
				StickerFormatType.Lottie => $"{sticker.Name}.lottie",
				(StickerFormatType)4 => $"{sticker.Name}.gif",
				_ => throw new InvalidOperationException($"Invalid StickerFormatType {sticker.Format}")
			};

			return new FileAttachment(ms, name);
		}

		public async Task<bool> TrySendMessageAsync(Message message, ISocketMessageChannel channel, GuildConfig config)
		{
			if (string.IsNullOrEmpty(message.Text))
				return false;
			
			switch (message.Type)
			{
				case MessageType.Raw:
					await channel.SendMessageAsync(message.Text, allowedMentions: config.GetAllowedMentions());
					return true;
				case MessageType.Sticker:
					ulong[] stickerIds = JsonSerializer.Deserialize<ulong[]>(message.Text) ?? throw new InvalidOperationException("Invalid sticker id");
					return await HandleStickers(channel, stickerIds, config.GetAllowedMentions());
				default:
					return false;
			};
		}

		public async Task SendRandomMessageAsync(ISocketMessageChannel channel, GuildConfig config)
		{
			using var typing = channel.EnterTypingState();
			Message m = RetrieveRandomMessage(config);
			try
			{
				while (!await TrySendMessageAsync(m, channel, config))
				{
					m = RetrieveRandomMessage(config);
				}
			}
			catch (Exception e)
			{
				string message = $"An error occurred while sending a random message\n{nameof(m.MessageId)}: {m.MessageId}, {nameof(m.Type)} {m.Type}, {nameof(m.Index)}: {m.Index}, {nameof(m.Text)}: '{m.Text}'\n";
				throw new Exception(message, e);
			}
		}

		public bool TryRetrieveRandomMessage(GuildConfig config, [NotNullWhen(true)] out Message? message)
		{
			if (config.Messages.Count != 0)
			{
				int next = _random.Next(config.TargetMessageCount);
				message = config.Messages.First(m => m.Index == next);
				return true;
			}

			message = null;
			return false;
		}

		public Message RetrieveRandomMessage(GuildConfig config)
		{
			if (TryRetrieveRandomMessage(config, out Message? message))
				return message;
			else
				return Message.Empty;
		}

		private static bool TestMessageStorable(IMessage message, GuildConfig config)
		{
			if (message.Type != Discord.MessageType.Default)
				return false;
            return message.Author.Id == config.TargetUserId;
		}

		private sealed class ConcurrentUpdateData
		{
			public long Processed;
			public int TargetMessageCount;
			public bool IsFinished;
			public int RunningThreads;
		}

		internal static async Task ReadAllMessages(ReplicatorContext context, DiscordSocketClient client, ILogger logger, SocketGuild guild, int maxChannelRead, ISocketMessageChannel reply)
		{
			GuildConfig config = context.GuildConfig.Include(g => g.ChannelPermissions).First(g => g.GuildId == guild.Id);

			config.Enabled = false;
			context.GuildConfig.Update(config);
			context.SaveChanges();

			config.GuildMessageCount = 0;
			config.LastUpdate = default;
			config.TargetMessageCount = 0;

			if (config.TargetUserId is null)
			{
				await reply.SendMessageAsync("Target user not selected");
				return;
			}

			context.Messages.RemoveRange(context.Messages.AsQueryable().Where(c => c.GuildId == guild.Id));
			int messageIndex = 0;

			SocketGuildUser currentUser = guild.GetUser(client.CurrentUser.Id);
			foreach (var channel in guild.TextChannels)
			{
				RestUserMessage responseMessage = await reply.SendMessageAsync($"Attempting to read messages in channel {channel.Mention}");

				Discord.ChannelPermissions permissions = currentUser.GetPermissions(channel);
				ChannelPermissions perms = config.ChannelPermissions.FirstOrDefault(c => c.ChannelId == channel.Id) ??
					ChannelPermissions.CreateDefault(context, guild.Id, channel.Id);

				if (!permissions.ReadMessageHistory || !permissions.ViewChannel || !perms.Permissions.HasFlag(ChannelPermission.Read))
				{
					await reply.SendMessageAsync($"No permission to read in channel {channel.Mention}").ConfigureAwait(false);
					continue;
				}

				RequestOptions.Default.RetryMode = RetryMode.AlwaysRetry;
				var messageCollection = channel.GetMessagesAsync(maxChannelRead, options: RequestOptions.Default);

				const int processedIncrement = 10;

				ConcurrentUpdateData data = new ConcurrentUpdateData
				{
					Processed = 0,
					TargetMessageCount = 0,
					IsFinished = false,
					RunningThreads = 0
				};

				ConcurrentQueue<Message> messages = [];

				Thread updateThread = new Thread(async (d) =>
				{
					ConcurrentUpdateData concurrentUpdateData = d as ConcurrentUpdateData ?? throw new InvalidOperationException("Data was not correctly passed to thread");

					long nextProcessed = processedIncrement;
					while (!data.IsFinished)
					{
						long processed = Interlocked.Read(ref data.Processed);
						if (processed >= nextProcessed)
						{
							await responseMessage.ModifyAsync(m => m.Content = $"{channel.Mention}: Processed {processed} messages.").ConfigureAwait(false);
							while (nextProcessed <= processed)
								nextProcessed += processedIncrement;
						}
						else
							Thread.Sleep(20);
					}
				})
				{
					Priority = ThreadPriority.AboveNormal,
					IsBackground = false,
					Name = "UpdateThread"
				};
				updateThread.Start(data);

				Thread[] threadPool = new Thread[8];
				IReadOnlyCollection<IMessage>?[] pages = new IReadOnlyCollection<IMessage>[threadPool.Length];

				void ProcessPage(object? p)
				{
					int i = (int)p!;

					while (!data.IsFinished)
					{
						while (pages[i] is null)
						{
							if (data.IsFinished)
								return;

							if (Array.IndexOf(pages, null) == -1)
								Thread.Yield();
							else
								Thread.Sleep(20);
						}

						IReadOnlyCollection<IMessage> page = pages[i]!;

						Interlocked.Increment(ref data.RunningThreads);

						foreach (var message in page)
						{
							Interlocked.Increment(ref data.Processed);
							if (TestMessageStorable(message, config))
							{
								Interlocked.Increment(ref data.TargetMessageCount);
								messages.Enqueue(AddNewMessageConcurrent(message, config));
							}
						}

						int j = Array.IndexOf(pages, null);
						pages[i] = null;
						Interlocked.Decrement(ref data.RunningThreads);

						if (j == -1)
							Thread.Yield();
						else
							Thread.Sleep(20);
					}
				}

				for (int i = 0; i < threadPool.Length; i++)
				{
					threadPool[i] = new Thread(ProcessPage)
					{
						IsBackground = true,
						Name = $"ReadMessagesThread-{i}"
					};

					threadPool[i].Start(i);
				}

				await foreach (var page in messageCollection)
				{
					int i = Array.IndexOf(pages, null);

					while (i == -1)
					{
						Thread.Sleep(20);
						i = Array.IndexOf(pages, null);
					}

					pages[i] = page;
				}

				foreach (var t in threadPool.Where(t => t.ThreadState == ThreadState.Running))
					t.Join();

				config.GuildMessageCount += (int)data.Processed;
				config.TargetMessageCount += data.TargetMessageCount;

				if (config.AutoUpdateProbability)
					config.Probability = (double)config.TargetMessageCount / config.GuildMessageCount;

				context.GuildConfig.Update(config);

				foreach (var message in messages)
				{
					message.Index = messageIndex++;
				}

				context.Messages.AddRange(messages.Where(m => !string.IsNullOrEmpty(m.Text)));

				data.IsFinished = true;
				updateThread.Join();

				await responseMessage.ModifyAsync(m => m.Content = $"{channel.Mention}: Processed {data.Processed} messages.").ConfigureAwait(false);
			}

			config.Enabled = true;
			config.LastUpdate = DateTime.UtcNow;
			context.GuildConfig.Update(config);

			await context.SaveChangesAsync();

			await reply.SendMessageAsync($"Read all messages and {currentUser.Mention} is now active");
		}

		internal static async Task ReadSinceTimestamp(ReplicatorContext context, DiscordSocketClient client, SocketGuild guild, DateTime lastReceivedTime, ISocketMessageChannel reply)
		{
			GuildConfig config = context.GuildConfig.Include(g => g.ChannelPermissions).First(g => g.GuildId == guild.Id);
			HashSet<ulong> messageIds = context.Messages.AsQueryable().Where(m => m.GuildId == guild.Id).Select(m => m.MessageId).ToHashSet();

			IGuildUser currentUser = guild.GetUser(client.CurrentUser.Id);
			foreach (var channel in guild.TextChannels)
			{
				await reply.SendMessageAsync($"Reading new messages in channel {channel.Mention}");

				Discord.ChannelPermissions permissions = currentUser.GetPermissions(channel);
				ChannelPermissions perms = config.ChannelPermissions.FirstOrDefault(c => c.ChannelId == channel.Id) ??
					ChannelPermissions.CreateDefault(context, guild.Id, channel.Id);

				if (permissions.ReadMessageHistory && permissions.ViewChannel && perms.Permissions.HasFlag(ChannelPermission.Read))
				{
					IMessage lastMessage = (await channel.GetMessagesAsync(1).Flatten().FirstAsync());
					DateTime currentTime = lastMessage.Timestamp.UtcDateTime;

					if (lastMessage is not null)
					{
						while (currentTime >= lastReceivedTime)
						{
							var messageCollection = channel.GetMessagesAsync(lastMessage, Direction.Before, 10).Flatten();
							await foreach (var message in messageCollection)
							{
								if (string.IsNullOrEmpty(message.Content) || message.Content[0] != '!')
								{
									lastMessage = message;
									currentTime = lastMessage.Timestamp.UtcDateTime;

									if (currentTime >= lastReceivedTime)
										break;

									if (messageIds.Contains(lastMessage.Id))
										break;

									config.GuildMessageCount += 1;
									if (TestMessageStorable(message, config))
										AddNewMessage(context, client, lastMessage, config, false);
								}
							}
						}
					}
				}
				else
					await reply.SendMessageAsync($"No permission to read in channel {channel.Mention}");
			}

			config.Enabled = true;
			config.LastUpdate = DateTime.UtcNow;
			context.GuildConfig.Update(config);

			await context.SaveChangesAsync();

			await reply.SendMessageAsync("Read all new messages");
		}

		private async Task GuildAvailableAsync(SocketGuild guild)
		{
			await AddGuildAsync(guild);
			Logger.LogInformation("Server {name} ({id}) became available", guild.Name, guild.Id);

		}
		private async Task GuildUnavailableAsync(SocketGuild guild)
		{
			await AddGuildAsync(guild);
			Logger.LogInformation("Server {name} ({id}) became unavailable", guild.Name, guild.Id);
		}
		private async Task JoinedGuildAsync(SocketGuild guild)
		{
			await AddGuildAsync(guild);
			Logger.LogInformation("Joined Server {name} ({id})", guild.Name, guild.Id);
		}
		private async Task LeftGuildAsync(SocketGuild guild)
		{
			await RemoveGuildAsync(guild);
			Logger.LogInformation("Left Server {name} ({id})", guild.Name, guild.Id);
		}

		private async Task AddGuildAsync(SocketGuild guild)
		{
			using var scope = Services.CreateScope();
			using var context = scope.ServiceProvider.GetRequiredService<ReplicatorContext>();

			if (!await context.Guild.AsAsyncEnumerable().Select(g => g.GuildId).ContainsAsync(guild.Id))
			{
				context.Guild.Add(new Guild(guild.Id));
				context.GuildConfig.Add(new GuildConfig(guild.Id));
				context.ChannelPermissions.AddRange(guild.Channels.Select(c => new ChannelPermissions(guild.Id, c.Id, ChannelPermission.ReadWrite)));

				await context.SaveChangesAsync();
			}
		}
		private async Task RemoveGuildAsync(SocketGuild guild)
		{
			using var scope = Services.CreateScope();
			using var context = scope.ServiceProvider.GetRequiredService<ReplicatorContext>();

			GuildConfig? info = await context.GuildConfig.AsAsyncEnumerable().Where(g => g.GuildId == guild.Id).FirstOrDefaultAsync();
			if (info is not null)
			{
				context.Messages.RemoveRange(info.Messages);
				context.ChannelPermissions.RemoveRange(info.ChannelPermissions);
				context.DisabledUsers.RemoveRange(info.DisabledUsers);
				context.GuildConfig.Remove(info);
			}

			Guild g = await context.Guild.AsAsyncEnumerable().FirstAsync(g => g.GuildId == guild.Id);
			context.Guild.Remove(g);

			await context.SaveChangesAsync();
		}

		private async Task LogAsync(LogMessage message) => await Task.Run(() => Logger.Log(message.Severity.ToLogLevel(), "{message}", message));

		public void EnqueueReadAllOperation(ulong guildId, ulong responseChannelId, int maxMessages)
		{
			ReadOperationData data = new ReadOperationData(Client, Services, Logger, guildId, responseChannelId, maxMessages);
			Thread readAllThread = new Thread(ReadAllThread)
			{
				Name = "ReadAllMessagesOperation",
				IsBackground = false
			};
			readAllThread.Start(data);
		}

		public void EnqueueReadUpdateOperation(ulong guildId, ulong responseChannelId)
		{
			ReadOperationData data = new ReadOperationData(Client, Services, Logger, guildId, responseChannelId);
			Thread readUpdateThread = new Thread(ReadUpdateThread)
			{
				Name = "ReadUpdateMessagesOperation",
				IsBackground = true
			};
			readUpdateThread.Start(data);
		}

		private static async void ReadAllThread(object? parameter)
		{
			ReadOperationData data = parameter as ReadOperationData ?? throw new InvalidOperationException("Data was not correctly passed to thread");

			using var scope = data.Services.CreateScope();
			using var context = scope.ServiceProvider.GetRequiredService<ReplicatorContext>();

			SocketGuild guild = data.Client.GetGuild(data.GuildId);
			SocketTextChannel channel = guild.GetTextChannel(data.ResponseChannelId);

			await ReadAllMessages(context, data.Client, data.Logger, guild, data.MaxMessages, channel);
		}

		private static async void ReadUpdateThread(object? parameter)
		{
			ReadOperationData data = parameter as ReadOperationData ?? throw new InvalidOperationException("Data was not correctly passed to thread");

			using var scope = data.Services.CreateScope();
			using var context = scope.ServiceProvider.GetRequiredService<ReplicatorContext>();

			GuildConfig config = GuildConfig.Get(context, data.GuildId)!;

			SocketGuild guild = data.Client.GetGuild(data.GuildId);
			SocketTextChannel channel = guild.GetTextChannel(data.ResponseChannelId);

			await ReadSinceTimestamp(context, data.Client, guild, config.LastUpdate, channel);
		}

		private record ReadOperationData(DiscordSocketClient Client, IServiceProvider Services, ILogger Logger, ulong GuildId, ulong ResponseChannelId, int MaxMessages = -1);
	}
}
