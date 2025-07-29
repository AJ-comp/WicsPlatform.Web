using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("map_media_group")]
    public partial class MapMediaGroup
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("media_id")]
        [Required]
        public ulong MediaId { get; set; }

        public Medium Medium { get; set; }

        [Column("group_id")]
        [Required]
        public ulong GroupId { get; set; }

        public Group Group { get; set; }

        [Column("last_yn")]
        public string LastYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }
    }
}