using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("media")]
    public partial class Medium
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("file_name")]
        [Required]
        [MaxLength(100)]
        public string FileName { get; set; }

        [Column("full_path")]
        [Required]
        [MaxLength(200)]
        public string FullPath { get; set; }

        [Column("delete_yn")]
        [MaxLength(1)]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        public ICollection<MapChannelMedium> MapChannelMedia { get; set; }

        public ICollection<MapMediaGroup> MapMediaGroups { get; set; }
    }
}