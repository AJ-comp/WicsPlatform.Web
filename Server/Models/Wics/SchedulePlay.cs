using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("schedule_play")]
    public partial class SchedulePlay
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("schedule_id")]
        [Required]
        public ulong ScheduleId { get; set; }

        public Schedule Schedule { get; set; }

        [Column("media_id")]
        public ulong? MediaId { get; set; }

        [Column("tts_id")]
        public ulong? TtsId { get; set; }

        [Column("delay")]
        [Required]
        public ulong Delay { get; set; }

        [Column("delete_yn")]
        [MaxLength(1)]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }
    }
}