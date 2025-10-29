using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("group")]
    public partial class Group
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
        [MaxLength(50)]
        public string Name { get; set; }

        [Column("description")]
        [Required]
        [MaxLength(200)]
        public string Description { get; set; }

        [Column("delete_yn")]
        [MaxLength(1)]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        public ICollection<MapMediaGroup> MapMediaGroups { get; set; }

        public ICollection<MapSpeakerGroup> MapSpeakerGroups { get; set; }

        public ICollection<MapChannelGroup> MapChannelGroups { get; set; }
    }
}