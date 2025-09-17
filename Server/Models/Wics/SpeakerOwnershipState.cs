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
        [Column("speaker_id")]
        [Required]
        public ulong SpeakerId { get; set; }

        [Column("channel_id")]
        [Required]
        public ulong ChannelId { get; set; }

        [Column("ownership")]
        public string Ownership { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }
    }
}