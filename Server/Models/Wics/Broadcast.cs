using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace WicsPlatform.Server.Models.wics
{
    [Table("broadcast")]
    public partial class Broadcast
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("channel_id")]
        [Required]
        public ulong ChannelId { get; set; }

        public Channel Channel { get; set; }

        [Column("speaker_id_list")]
        public string SpeakerIdList { get; set; }

        [Column("media_id_list")]
        public string MediaIdList { get; set; }

        [Column("tts_id_list")]
        public string TtsIdList { get; set; }

        [Column("loopback_yn")]
        public string LoopbackYn { get; set; }

        [Column("ongoing_yn")]
        public string OngoingYn { get; set; }

        [Column("created_at")]
        [Required]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        [Required]
        public DateTime UpdatedAt { get; set; }

        // ✅ 띄어쓰기 구분 문자열 편의 프로퍼티 (NULL 대신 공백 문자열 저장)
        [NotMapped]
        public List<ulong> SpeakerIds
        {
            get => string.IsNullOrEmpty(SpeakerIdList) 
                ? new List<ulong>() 
                : SpeakerIdList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                               .Select(id => ulong.Parse(id.Trim()))
                               .ToList();
            set => SpeakerIdList = value?.Count > 0 
                ? string.Join(" ", value) 
                : "";  // ✅ NULL 대신 공백 문자열 저장
        }

        [NotMapped]
        public List<ulong> MediaIds
        {
            get => string.IsNullOrEmpty(MediaIdList) 
                ? new List<ulong>() 
                : MediaIdList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(id => ulong.Parse(id.Trim()))
                             .ToList();
            set => MediaIdList = value?.Count > 0 
                ? string.Join(" ", value) 
                : "";  // ✅ NULL 대신 공백 문자열 저장
        }

        [NotMapped]
        public List<ulong> TtsIds
        {
            get => string.IsNullOrEmpty(TtsIdList) 
                ? new List<ulong>() 
                : TtsIdList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .Select(id => ulong.Parse(id.Trim()))
                           .ToList();
            set => TtsIdList = value?.Count > 0 
                ? string.Join(" ", value) 
                : "";  // ✅ NULL 대신 공백 문자열 저장
        }
    }
}