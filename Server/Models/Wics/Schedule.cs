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

        [Column("start_time")]
        [Required]
        public TimeOnly StartTime { get; set; }

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

        [Column("last_execute_at")]
        public DateTime? LastExecuteAt { get; set; }

        public ICollection<Channel> Channels { get; set; }

        public ICollection<SchedulePlay> SchedulePlays { get; set; }
    }
}