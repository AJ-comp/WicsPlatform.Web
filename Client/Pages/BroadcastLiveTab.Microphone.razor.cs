using Microsoft.JSInterop;
using Radzen;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Shared;

namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    #region Microphone Configuration

    private MicConfig _micConfig = new MicConfig();

    #endregion

    #region Public Microphone Status Methods

    /// <summary>
    /// ����ũ�� ���� Ȱ��ȭ�Ǿ� �ִ��� Ȯ��
    /// </summary>
    public async Task<bool> IsMicrophoneActive()
    {
        try
        {
            if (_mixerModule == null)
            {
                _logger.LogDebug("Mixer module is null");
                return false;
            }

            var isActive = await _mixerModule.InvokeAsync<bool>("isMicrophoneEnabled");
            _logger.LogDebug($"Microphone active status: {isActive}");
            return isActive;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check microphone status");
            return false;
        }
    }

    #endregion

    #region Audio Mixer Initialization

    /// <summary>
    /// ����� �ͼ� �ʱ�ȭ �� ����ũ ����
    /// </summary>
    private async Task<bool> InitializeAudioMixer()
    {
        try
        {
            // ����ũ ���� Ȯ��
            if (await IsMicrophoneActive()) return true;

            // �ͼ� ����� ���� ���� import
            if (_mixerModule == null)
            {
                _mixerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/audiomixer.js");
            }

            var configWithVolume = CreateMicrophoneConfig();

            var success = await _mixerModule.InvokeAsync<bool>("createMixer", _dotNetRef, configWithVolume);

            if (!success)
            {
                NotifyError("����� �ͼ� �ʱ�ȭ ����", new Exception("����� �ͼ��� �ʱ�ȭ�� �� �����ϴ�."));
                return false;
            }

            LogMicrophoneInitialization();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "����� �ͼ� �ʱ�ȭ ����");
            NotifyError("����� �ͼ� �ʱ�ȭ ����", ex);
            return false;
        }
    }

    /// <summary>
    /// ����ũ ���� ��ü ����
    /// </summary>
    private Dictionary<string, object> CreateMicrophoneConfig()
    {
        return new Dictionary<string, object>
        {
            { "sampleRate", _micConfig.SampleRate },
            { "channels", _micConfig.Channels },
            { "timeslice", _micConfig.TimesliceMs },
            { "bitrate", _micConfig.Bitrate },
            { "echoCancellation", _micConfig.EchoCancellation },
            { "noiseSuppression", _micConfig.NoiseSuppression },
            { "autoGainControl", _micConfig.AutoGainControl },
            { "localPlayback", _micConfig.LocalPlayback },
            { "samplesPerSend", _micConfig.GetSamplesPerTimeslice() },
            { "micVolume", micVolume / 100.0 },
            { "mediaVolume", mediaVolume / 100.0 },
            { "ttsVolume", ttsVolume / 100.0 }
        };
    }

    /// <summary>
    /// ����ũ �ʱ�ȭ �α�
    /// </summary>
    private void LogMicrophoneInitialization()
    {
        _logger.LogInformation($"����� �ͼ� �ʱ�ȭ �Ϸ� - Mic: {_micConfig.SampleRate}Hz/{_micConfig.Channels}ch, Timeslice: {_micConfig.TimesliceMs}ms");
        LoggingService.AddLog("SUCCESS", $"����ũ �ʱ�ȭ �Ϸ� ({_micConfig.SampleRate}Hz/���)");
    }

    #endregion

    #region Microphone Activation

    /// <summary>
    /// ����ũ Ȱ��ȭ
    /// </summary>
    private async Task<bool> EnableMicrophone()
    {
        try
        {
            if (_mixerModule == null)
            {
                _logger.LogError("�ͼ� ����� �ʱ�ȭ���� �ʾҽ��ϴ�.");
                return false;
            }

            // ����ũ ���� Ȯ�� �Լ� ȣ��
            if (await IsMicrophoneActive())
            {
                _logger.LogInformation("����ũ�� �̹� Ȱ��ȭ�Ǿ� �ֽ��ϴ�.");
                LoggingService.AddLog("INFO", "����ũ �̹� Ȱ��ȭ��");
                return true;
            }

            var micEnabled = await _mixerModule.InvokeAsync<bool>("enableMic");

            if (!micEnabled)
            {
                NotifyWarn("����ũ Ȱ��ȭ ����", "����ũ ������ Ȯ�����ּ���.");
                return false;
            }

            LoggingService.AddLog("SUCCESS", "����ũ Ȱ��ȭ �Ϸ�");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "����ũ Ȱ��ȭ ����");
            NotifyError("����ũ Ȱ��ȭ", ex);

            return false;
        }
    }

    #endregion

    #region Audio Data Processing

    /// <summary>
    /// �ͼ����� ĸó�� ����� ������ ó��
    /// </summary>
    [JSInvokable]
    public async Task OnMixedAudioCaptured(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data)) return;

        try
        {
            byte[] audioData = Convert.FromBase64String(base64Data);

            // 1. ��� ������Ʈ
            UpdateAudioStatistics(audioData);

            // 2. ���� ������ ó��
            ProcessRecordingData(audioData);

            // 3. ����͸� ���ǿ� ������ ����
            await SendToMonitoringSection(audioData);

            // 4. WebSocket���� ����
            await SendToWebSocket(audioData);

            // 5. ������ ó��
            await ProcessLoopback(base64Data);

            // 6. UI ������Ʈ (100��Ŷ����)
            await UpdateUIIfNeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"OnMixedAudioCaptured ����: {ex.Message}");
        }
    }

    /// <summary>
    /// ����� ��� ������Ʈ
    /// </summary>
    private void UpdateAudioStatistics(byte[] data)
    {
        totalDataPackets++;
        totalDataSize += data.Length / 1024.0;
        audioLevel = CalculateAudioLevel(data);
    }

    /// <summary>
    /// ����� ���� ���
    /// </summary>
    private double CalculateAudioLevel(byte[] audioData)
    {
        if (audioData.Length < 2) return 0;

        double sum = 0;
        int sampleCount = audioData.Length / 2;

        for (int i = 0; i < audioData.Length - 1; i += 2)
        {
            short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
            double normalized = sample / 32768.0;
            sum += normalized * normalized;
        }

        double rms = Math.Sqrt(sum / sampleCount);
        return Math.Min(100, rms * 100);
    }

    /// <summary>
    /// ����͸� ���ǿ� ����� ������ ����
    /// </summary>
    private async Task SendToMonitoringSection(byte[] audioData)
    {
        if (monitoringSection != null)
        {
            await monitoringSection.OnAudioCaptured(audioData);
        }
    }

    /// <summary>
    /// WebSocket���� ����� ������ ����
    /// </summary>
    private async Task SendToWebSocket(byte[] audioData)
    {
        if (currentBroadcastId.HasValue)
        {
            await WebSocketService.SendAudioDataAsync(currentBroadcastId.Value, audioData);
        }
    }

    /// <summary>
    /// ������ ó��
    /// </summary>
    private async Task ProcessLoopback(string base64Data)
    {
        if (_currentLoopbackSetting)
        {
            await _speakerModule.InvokeVoidAsync("feed", base64Data);
        }
    }

    /// <summary>
    /// UI ������Ʈ (100��Ŷ����)
    /// </summary>
    private async Task UpdateUIIfNeeded()
    {
        if (totalDataPackets % 100 == 0)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    #endregion

    #region Microphone Help

    /// <summary>
    /// ����ũ ���� ǥ��
    /// </summary>
    [JSInvokable]
    public Task ShowMicHelp()
    {
        DialogService.Open<MicHelpDialog>("����ũ ���� ���� ���",
            new Dictionary<string, object>(),
            new DialogOptions { Width = "600px", Resizable = true });
        return Task.CompletedTask;
    }

    #endregion

    #region Microphone Cleanup

    /// <summary>
    /// ����ũ �� ����� �ͼ� ����
    /// </summary>
    private async Task CleanupMicrophone()
    {
        try
        {
            if (_mixerModule == null) return;

            try
            {
                await _mixerModule.InvokeVoidAsync("dispose");
                await _mixerModule.DisposeAsync();
                _logger.LogInformation("�ͼ� ��� ���� �Ϸ�");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�ͼ� ��� ���� ����");
            }
            finally
            {
                _mixerModule = null;
            }

            // _speakerModule�� �������� ���� (����)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "����ũ ���� �� ���� �߻�");
        }
    }

    #endregion

    #region Microphone Volume Control

    /// <summary>
    /// �ǽð� ����ũ ���� ����
    /// </summary>
    public async Task SetMicrophoneVolume(float volume)
    {
        if (_mixerModule == null) return;

        await _mixerModule.InvokeVoidAsync("setVolumes",
            volume,
            mediaVolume / 100.0,
            ttsVolume / 100.0);
    }

    /// <summary>
    /// ��ü ���� ���� ������Ʈ
    /// </summary>
    public async Task UpdateAllVolumes()
    {
        if (_mixerModule == null) return;

        await _mixerModule.InvokeVoidAsync("setVolumes",
            micVolume / 100.0,
            mediaVolume / 100.0,
            ttsVolume / 100.0);
    }

    #endregion
}
