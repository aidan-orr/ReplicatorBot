﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ReplicatorBot;

#nullable disable

namespace ReplicatorBot.Migrations
{
    [DbContext(typeof(ReplicatorContext))]
    partial class ReplicatorContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Proxies:ChangeTracking", false)
                .HasAnnotation("Proxies:CheckEquality", false)
                .HasAnnotation("Proxies:LazyLoading", true);

            modelBuilder.Entity("DiscordBotCore.Guild", b =>
                {
                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.HasKey("GuildId");

                    b.ToTable("Guild");
                });

            modelBuilder.Entity("ReplicatorBot.ChannelPermissions", b =>
                {
                    b.Property<long>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("ChannelId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Permissions")
                        .HasColumnType("INTEGER");

                    b.HasKey("GuildId", "ChannelId");

                    b.ToTable("ChannelPermissions");
                });

            modelBuilder.Entity("ReplicatorBot.DisabledUser", b =>
                {
                    b.Property<long>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("UserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("GuildId", "UserId");

                    b.ToTable("DisabledUsers");
                });

            modelBuilder.Entity("ReplicatorBot.GuildConfig", b =>
                {
                    b.Property<ulong>("GuildId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<bool>("AutoUpdateMessages")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("AutoUpdateProbability")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("CanMention")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Enabled")
                        .HasColumnType("INTEGER");

                    b.Property<int>("GuildMessageCount")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("LastUpdate")
                        .HasColumnType("TEXT");

                    b.Property<double>("Probability")
                        .HasColumnType("REAL");

                    b.Property<int>("TargetMessageCount")
                        .HasColumnType("INTEGER");

                    b.Property<long?>("TargetUserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("GuildId");

                    b.ToTable("GuildConfig");
                });

            modelBuilder.Entity("ReplicatorBot.Message", b =>
                {
                    b.Property<long>("MessageId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Text")
                        .IsRequired()
                        .HasMaxLength(6144)
                        .HasColumnType("TEXT");

                    b.Property<int>("Type")
                        .HasColumnType("INTEGER");

                    b.HasKey("MessageId");

                    b.HasIndex(new[] { "GuildId", "Index" }, "IX_Message_Guild_Index")
                        .IsUnique();

                    b.ToTable("Messages");
                });

            modelBuilder.Entity("DiscordBotCore.Guild", b =>
                {
                    b.HasOne("ReplicatorBot.GuildConfig", null)
                        .WithOne("Guild")
                        .HasForeignKey("DiscordBotCore.Guild", "GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("ReplicatorBot.ChannelPermissions", b =>
                {
                    b.HasOne("ReplicatorBot.GuildConfig", "GuildConfig")
                        .WithMany("ChannelPermissions")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildConfig");
                });

            modelBuilder.Entity("ReplicatorBot.DisabledUser", b =>
                {
                    b.HasOne("ReplicatorBot.GuildConfig", "GuildConfig")
                        .WithMany("DisabledUsers")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildConfig");
                });

            modelBuilder.Entity("ReplicatorBot.Message", b =>
                {
                    b.HasOne("ReplicatorBot.GuildConfig", "GuildConfig")
                        .WithMany("Messages")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildConfig");
                });

            modelBuilder.Entity("ReplicatorBot.GuildConfig", b =>
                {
                    b.Navigation("ChannelPermissions");

                    b.Navigation("DisabledUsers");

                    b.Navigation("Guild");

                    b.Navigation("Messages");
                });
#pragma warning restore 612, 618
        }
    }
}
