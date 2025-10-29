using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("mic")]
    public partial class Mic
    {
        [Key]
        [Column("id")]
        [Required]
        [MaxLength(50)]
        public string Id { get; set; }

        [Column("label")]
        [Required]
        [MaxLength(100)]
        public string Label { get; set; }

        [Column("samplerate")]
        public uint Samplerate { get; set; }

        [Column("channels")]
        public byte Channels { get; set; }

        [Column("bitrate")]
        public uint Bitrate { get; set; }
    }
}