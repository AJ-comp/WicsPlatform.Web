using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("map_channel_tts")]
    public partial class MapChannelTt
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("channel_id")]
        [Required]
        public ulong ChannelId { get; set; }

        public Channel Channel { get; set; }

        [Column("tts_id")]
        [Required]
        public ulong TtsId { get; set; }

        public Tt Tt { get; set; }

        [Column("delete_yn")]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }
    }
}