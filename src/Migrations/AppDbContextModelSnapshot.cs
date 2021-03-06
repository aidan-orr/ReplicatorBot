// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ReplicatorBot;

namespace ReplicatorBot.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "5.0.6");

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

            modelBuilder.Entity("ReplicatorBot.DisabledSubstring", b =>
                {
                    b.Property<long>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Substring")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.HasKey("GuildId", "Index");

                    b.ToTable("DisabledSubstrings");
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

            modelBuilder.Entity("ReplicatorBot.GuildInfo", b =>
                {
                    b.Property<long>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("AutoUpdateMessages")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("AutoUpdateProbability")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("CanEmbed")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("CanMention")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Delay")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Enabled")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("FixedDelay")
                        .HasColumnType("INTEGER");

                    b.Property<int>("GuildMessageCount")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("LastUpdate")
                        .HasColumnType("TEXT");

                    b.Property<string>("Prefix")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("TEXT");

                    b.Property<double>("Probability")
                        .HasColumnType("REAL");

                    b.Property<int>("TargetMessageCount")
                        .HasColumnType("INTEGER");

                    b.Property<long?>("TargetUserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("GuildId");

                    b.ToTable("GuildInfo");
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
                        .HasMaxLength(2048)
                        .HasColumnType("TEXT");

                    b.HasKey("MessageId");

                    b.HasIndex(new[] { "GuildId", "Index" }, "IX_Message_Guild_Index")
                        .IsUnique();

                    b.ToTable("Messages");
                });

            modelBuilder.Entity("ReplicatorBot.ChannelPermissions", b =>
                {
                    b.HasOne("ReplicatorBot.GuildInfo", "GuildInfo")
                        .WithMany("ChannelPermissions")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildInfo");
                });

            modelBuilder.Entity("ReplicatorBot.DisabledSubstring", b =>
                {
                    b.HasOne("ReplicatorBot.GuildInfo", "GuildInfo")
                        .WithMany("DisabledSubstrings")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildInfo");
                });

            modelBuilder.Entity("ReplicatorBot.DisabledUser", b =>
                {
                    b.HasOne("ReplicatorBot.GuildInfo", "GuildInfo")
                        .WithMany("DisabledUsers")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildInfo");
                });

            modelBuilder.Entity("ReplicatorBot.Message", b =>
                {
                    b.HasOne("ReplicatorBot.GuildInfo", "GuildInfo")
                        .WithMany("Messages")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GuildInfo");
                });

            modelBuilder.Entity("ReplicatorBot.GuildInfo", b =>
                {
                    b.Navigation("ChannelPermissions");

                    b.Navigation("DisabledSubstrings");

                    b.Navigation("DisabledUsers");

                    b.Navigation("Messages");
                });
#pragma warning restore 612, 618
        }
    }
}
