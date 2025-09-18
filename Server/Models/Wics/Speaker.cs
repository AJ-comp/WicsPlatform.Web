using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WicsPlatform.Server.Models.wics
{
    [Table("speaker")]
    public partial class Speaker
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("ip")]
        [Required]
        public string Ip { get; set; }

        [Column("vpn_ip")]
        [Required]
        public string VpnIp { get; set; }

        [Column("model")]
        [Required]
        public string Model { get; set; }

        [Column("name")]
        [Required]
        public string Name { get; set; }

        [Column("password")]
        [Required]
        public string Password { get; set; }

        [Column("location")]
        [Required]
        public string Location { get; set; }

        [Column("state")]
        [Required]
        public byte State { get; set; }

        [Column("vpn_use_yn")]
        public string VpnUseYn { get; set; }

        [Column("ping_intv")]
        public ushort PingIntv { get; set; }

        [Column("delete_yn")]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        public ICollection<MapSpeakerGroup> MapSpeakerGroups { get; set; }

        public ICollection<SpeakerOwnershipState> SpeakerOwnershipStates { get; set; }
    }
}