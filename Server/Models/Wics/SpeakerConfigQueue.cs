using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("speaker_config_queue")]
    public partial class SpeakerConfigQueue
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("channel_id")]
        [Required]
        public ulong ChannelId { get; set; }

        public Channel Channel { get; set; }

        [Column("speaker_id")]
        [Required]
        public ulong SpeakerId { get; set; }

        public Speaker Speaker { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}