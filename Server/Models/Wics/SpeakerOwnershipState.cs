using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("speaker_ownership_state")]
    public partial class SpeakerOwnershipState
    {
        [Key]
        [Column("speaker_id")]
        [Required]
        public ulong SpeakerId { get; set; }

        public Speaker Speaker { get; set; }

        [Key]
        [Column("channel_id")]
        [Required]
        public ulong ChannelId { get; set; }

        public Channel Channel { get; set; }

        [Column("ownership")]
        [MaxLength(1)]
        public string Ownership { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }
    }
}