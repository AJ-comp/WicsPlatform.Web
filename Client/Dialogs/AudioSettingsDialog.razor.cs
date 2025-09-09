using Microsoft.AspNetCore.Components;
using Radzen;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WicsPlatform.Client.Dialogs
{
    public partial class AudioSettingsDialog
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsBroadcasting { get; set; }
        [Parameter] public int PreferredSampleRate { get; set; }
        [Parameter] public int PreferredChannels { get; set; }

        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected NotificationService NotificationService { get; set; }

        private int CurrentSampleRate => Channel?.SamplingRate > 0 ? (int)Channel.SamplingRate : PreferredSampleRate;
        private int CurrentChannels => Channel?.ChannelCount ?? PreferredChannels;

        private List<SampleRateOption> sampleRateOptions = new List<SampleRateOption>
        {
            new SampleRateOption { Value = 8000, Text = "8000 Hz (전화품질)" },
            new SampleRateOption { Value = 16000, Text = "16000 Hz (광대역)" },
            new SampleRateOption { Value = 24000, Text = "24000 Hz (고품질)" },
            new SampleRateOption { Value = 48000, Text = "48000 Hz (프로페셔널)" }
        };

        private List<ChannelOption> channelOptions = new List<ChannelOption>
        {
            new ChannelOption { Value = 1, Text = "모노 (1채널)" },
            new ChannelOption { Value = 2, Text = "스테레오 (2채널)" }
        };

        public class SampleRateOption
        {
            public int Value { get; set; }
            public string Text { get; set; }
        }

        public class ChannelOption
        {
            public int Value { get; set; }
            public string Text { get; set; }
        }

        protected override void OnInitialized()
        {
            // 현재 설정값 초기화
            if (Channel != null)
            {
                PreferredSampleRate = Channel.SamplingRate > 0 ? (int)Channel.SamplingRate : PreferredSampleRate;
                PreferredChannels = Channel.ChannelCount;
            }
        }

        private void Cancel()
        {
            DialogService.Close(false);
        }

        private void Apply()
        {
            if (IsBroadcasting)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Warning,
                    Summary = "변경 불가",
                    Detail = "방송 중에는 오디오 설정을 변경할 수 없습니다.",
                    Duration = 3000
                });
                return;
            }

            var result = new AudioSettingsResult
            {
                SampleRate = PreferredSampleRate,
                Channels = PreferredChannels
            };

            DialogService.Close(result);
        }
    }

    public class AudioSettingsResult
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
    }
}