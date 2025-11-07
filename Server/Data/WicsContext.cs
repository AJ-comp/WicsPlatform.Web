using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Server.Data
{
    public partial class wicsContext : DbContext
    {
        public wicsContext()
        {
        }

        public wicsContext(DbContextOptions<wicsContext> options) : base(options)
        {
        }

        partial void OnModelBuilding(ModelBuilder builder);

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>().HasKey(table => new {
                table.SpeakerId, table.ChannelId
            });

            builder.Entity<WicsPlatform.Server.Models.wics.Broadcast>()
              .HasOne(i => i.Channel)
              .WithMany(i => i.Broadcasts)
              .HasForeignKey(i => i.ChannelId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .HasOne(i => i.Schedule)
              .WithMany(i => i.Channels)
              .HasForeignKey(i => i.ScheduleId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelGroup>()
              .HasOne(i => i.Channel)
              .WithMany(i => i.MapChannelGroups)
              .HasForeignKey(i => i.ChannelId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelGroup>()
              .HasOne(i => i.Group)
              .WithMany(i => i.MapChannelGroups)
              .HasForeignKey(i => i.GroupId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelMedium>()
              .HasOne(i => i.Channel)
              .WithMany(i => i.MapChannelMedia)
              .HasForeignKey(i => i.ChannelId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelMedium>()
              .HasOne(i => i.Medium)
              .WithMany(i => i.MapChannelMedia)
              .HasForeignKey(i => i.MediaId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelSpeaker>()
              .HasOne(i => i.Channel)
              .WithMany(i => i.MapChannelSpeakers)
              .HasForeignKey(i => i.ChannelId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelSpeaker>()
              .HasOne(i => i.Speaker)
              .WithMany(i => i.MapChannelSpeakers)
              .HasForeignKey(i => i.SpeakerId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelTt>()
              .HasOne(i => i.Channel)
              .WithMany(i => i.MapChannelTts)
              .HasForeignKey(i => i.ChannelId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelTt>()
              .HasOne(i => i.Tt)
              .WithMany(i => i.MapChannelTts)
              .HasForeignKey(i => i.TtsId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapMediaGroup>()
              .HasOne(i => i.Group)
              .WithMany(i => i.MapMediaGroups)
              .HasForeignKey(i => i.GroupId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapMediaGroup>()
              .HasOne(i => i.Medium)
              .WithMany(i => i.MapMediaGroups)
              .HasForeignKey(i => i.MediaId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapSpeakerGroup>()
              .HasOne(i => i.Group)
              .WithMany(i => i.MapSpeakerGroups)
              .HasForeignKey(i => i.GroupId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.MapSpeakerGroup>()
              .HasOne(i => i.Speaker)
              .WithMany(i => i.MapSpeakerGroups)
              .HasForeignKey(i => i.SpeakerId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.SchedulePlay>()
              .HasOne(i => i.Schedule)
              .WithMany(i => i.SchedulePlays)
              .HasForeignKey(i => i.ScheduleId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>()
              .HasOne(i => i.Channel)
              .WithMany(i => i.SpeakerOwnershipStates)
              .HasForeignKey(i => i.ChannelId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>()
              .HasOne(i => i.Speaker)
              .WithMany(i => i.SpeakerOwnershipStates)
              .HasForeignKey(i => i.SpeakerId)
              .HasPrincipalKey(i => i.Id);

            builder.Entity<WicsPlatform.Server.Models.wics.Broadcast>()
              .Property(p => p.LoopbackYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.Codec)
              .HasDefaultValueSql(@"'OPUS'");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.SamplingRate)
              .HasDefaultValueSql(@"'24000'");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.ChannelCount)
              .HasDefaultValueSql(@"'1'");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.BitRate)
              .HasDefaultValueSql(@"'32000'");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.Priority)
              .HasDefaultValueSql(@"'255'");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.Description)
              .HasDefaultValueSql(@"''");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Group>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelGroup>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelMedium>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelSpeaker>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelTt>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapMediaGroup>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapMediaGroup>()
              .Property(p => p.LastYn)
              .HasDefaultValueSql(@"'Y'");

            builder.Entity<WicsPlatform.Server.Models.wics.MapSpeakerGroup>()
              .Property(p => p.LastYn)
              .HasDefaultValueSql(@"'Y'");

            builder.Entity<WicsPlatform.Server.Models.wics.Medium>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Mic>()
              .Property(p => p.Samplerate)
              .HasDefaultValueSql(@"'16000'");

            builder.Entity<WicsPlatform.Server.Models.wics.Mic>()
              .Property(p => p.Channels)
              .HasDefaultValueSql(@"'1'");

            builder.Entity<WicsPlatform.Server.Models.wics.Mic>()
              .Property(p => p.Bitrate)
              .HasDefaultValueSql(@"'32000'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Monday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Tuesday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Wednesday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Thursday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Friday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Saturday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.Sunday)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.RepeatCount)
              .HasDefaultValueSql(@"'1'");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.SchedulePlay>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Speaker>()
              .Property(p => p.UdpPort)
              .HasDefaultValueSql(@"'6001'");

            builder.Entity<WicsPlatform.Server.Models.wics.Speaker>()
              .Property(p => p.VpnUseYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Speaker>()
              .Property(p => p.PingIntv)
              .HasDefaultValueSql(@"'30'");

            builder.Entity<WicsPlatform.Server.Models.wics.Speaker>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>()
              .Property(p => p.Ownership)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Tt>()
              .Property(p => p.DeleteYn)
              .HasDefaultValueSql(@"'N'");

            builder.Entity<WicsPlatform.Server.Models.wics.Broadcast>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Broadcast>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Channel>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Group>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Group>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelGroup>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelGroup>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelMedium>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelMedium>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelSpeaker>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelSpeaker>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelTt>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapChannelTt>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapMediaGroup>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapMediaGroup>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapSpeakerGroup>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.MapSpeakerGroup>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Medium>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Medium>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Schedule>()
              .Property(p => p.LastExecuteAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.SchedulePlay>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.SchedulePlay>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Speaker>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Speaker>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.SpeakerOwnershipState>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Tt>()
              .Property(p => p.CreatedAt)
              .HasColumnType("datetime");

            builder.Entity<WicsPlatform.Server.Models.wics.Tt>()
              .Property(p => p.UpdatedAt)
              .HasColumnType("datetime");
            this.OnModelBuilding(builder);
        }

        public DbSet<WicsPlatform.Server.Models.wics.Broadcast> Broadcasts { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Channel> Channels { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Group> Groups { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.MapChannelGroup> MapChannelGroups { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.MapChannelMedium> MapChannelMedia { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.MapChannelSpeaker> MapChannelSpeakers { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.MapChannelTt> MapChannelTts { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.MapMediaGroup> MapMediaGroups { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.MapSpeakerGroup> MapSpeakerGroups { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Medium> Media { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Mic> Mics { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Schedule> Schedules { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.SchedulePlay> SchedulePlays { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Speaker> Speakers { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.SpeakerOwnershipState> SpeakerOwnershipStates { get; set; }

        public DbSet<WicsPlatform.Server.Models.wics.Tt> Tts { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Conventions.Add(_ => new BlankTriggerAddingConvention());
        }
    }
}