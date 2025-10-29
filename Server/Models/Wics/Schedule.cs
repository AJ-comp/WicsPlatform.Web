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
        [MaxLength(1)]
        public string Monday { get; set; }

        [Column("tuesday")]
        [MaxLength(1)]
        public string Tuesday { get; set; }

        [Column("wednesday")]
        [MaxLength(1)]
        public string Wednesday { get; set; }

        [Column("thursday")]
        [MaxLength(1)]
        public string Thursday { get; set; }

        [Column("friday")]
        [MaxLength(1)]
        public string Friday { get; set; }

        [Column("saturday")]
        [MaxLength(1)]
        public string Saturday { get; set; }

        [Column("sunday")]
        [MaxLength(1)]
        public string Sunday { get; set; }

        [Column("repeat_count")]
        public byte RepeatCount { get; set; }

        [Column("delete_yn")]
        [MaxLength(1)]
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