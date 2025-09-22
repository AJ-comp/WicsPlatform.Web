using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("schedule")]
    public partial class Schedule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("name")]
        [Required]
        public string Name { get; set; }

        [Column("description")]
        [Required]
        public string Description { get; set; }

        [Column("sample_rate")]
        [Required]
        public uint SampleRate { get; set; }

        [Column("channel_count")]
        [Required]
        public byte ChannelCount { get; set; }

        [Column("volume")]
        [Required]
        public float Volume { get; set; }

        [Column("start_time")]
        [Required]
        public DateTime StartTime { get; set; }

        [Column("monday")]
        public string Monday { get; set; }

        [Column("tuesday")]
        public string Tuesday { get; set; }

        [Column("wednesday")]
        public string Wednesday { get; set; }

        [Column("thursday")]
        public string Thursday { get; set; }

        [Column("friday")]
        public string Friday { get; set; }

        [Column("saturday")]
        public string Saturday { get; set; }

        [Column("sunday")]
        public string Sunday { get; set; }

        [Column("repeat_count")]
        public byte RepeatCount { get; set; }

        [Column("delete_yn")]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        public ICollection<MapScheduleMedium> MapScheduleMedia { get; set; }

        public ICollection<MapScheduleTt> MapScheduleTts { get; set; }
    }
}