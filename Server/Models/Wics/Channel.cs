using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("channel")]
    public partial class Channel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("type")]
        [Required]
        public byte Type { get; set; }

        [Column("name")]
        [Required]
        public string Name { get; set; }

        [Column("mic_volume")]
        [Required]
        public float MicVolume { get; set; }

        [Column("tts_volume")]
        [Required]
        public float TtsVolume { get; set; }

        [Column("media_volume")]
        [Required]
        public float MediaVolume { get; set; }

        [Column("volume")]
        [Required]
        public float Volume { get; set; }

        [Column("audio_method")]
        [Required]
        public byte AudioMethod { get; set; }

        [Column("codec")]
        public string Codec { get; set; }

        [Column("sampling_rate")]
        public uint SamplingRate { get; set; }

        [Column("channel_count")]
        public byte ChannelCount { get; set; }

        [Column("bit_rate")]
        public uint BitRate { get; set; }

        [Column("priority")]
        public byte Priority { get; set; }

        [Column("state")]
        [Required]
        public sbyte State { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("delete_yn")]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        public ICollection<Broadcast> Broadcasts { get; set; }

        public ICollection<MapChannelMedium> MapChannelMedia { get; set; }

        public ICollection<MapChannelTt> MapChannelTts { get; set; }
    }
}