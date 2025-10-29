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
        [MaxLength(50)]
        public string Ip { get; set; }

        [Column("vpn_ip")]
        [Required]
        [MaxLength(50)]
        public string VpnIp { get; set; }

        [Column("udp_port")]
        public short UdpPort { get; set; }

        [Column("model")]
        [Required]
        [MaxLength(50)]
        public string Model { get; set; }

        [Column("name")]
        [Required]
        [MaxLength(50)]
        public string Name { get; set; }

        [Column("password")]
        [Required]
        [MaxLength(50)]
        public string Password { get; set; }

        [Column("location")]
        [Required]
        [MaxLength(50)]
        public string Location { get; set; }

        [Column("state")]
        [Required]
        public byte State { get; set; }

        [Column("vpn_use_yn")]
        [MaxLength(1)]
        public string VpnUseYn { get; set; }

        [Column("ping_intv")]
        public ushort PingIntv { get; set; }

        [Column("delete_yn")]
        [MaxLength(1)]
        public string DeleteYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        public ICollection<MapSpeakerGroup> MapSpeakerGroups { get; set; }

        public ICollection<MapChannelSpeaker> MapChannelSpeakers { get; set; }

        public ICollection<SpeakerOwnershipState> SpeakerOwnershipStates { get; set; }
    }
}