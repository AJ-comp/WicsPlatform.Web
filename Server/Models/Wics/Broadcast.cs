using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("broadcast")]
    public partial class Broadcast
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("channel_id")]
        [Required]
        public ulong ChannelId { get; set; }

        public Channel Channel { get; set; }

        [Column("speaker_id_list")]
        [MaxLength(100)]
        public string SpeakerIdList { get; set; }

        [Column("media_id_list")]
        [MaxLength(100)]
        public string MediaIdList { get; set; }

        [Column("tts_id_list")]
        [MaxLength(100)]
        public string TtsIdList { get; set; }

        [Column("loopback_yn")]
        [MaxLength(1)]
        public string LoopbackYn { get; set; }

        [Column("ongoing_yn")]
        [MaxLength(1)]
        public string OngoingYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }
    }
}