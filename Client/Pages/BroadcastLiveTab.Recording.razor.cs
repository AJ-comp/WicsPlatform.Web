namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    #region Recording Properties
    protected bool isRecording => RecordingService.IsRecording;
    protected string recordingDuration => RecordingService.RecordingDuration;
    protected double recordingDataSize => RecordingService.RecordingDataSize;
    #endregion

    #region Recording Control Methods
    protected async Task StartRecording()
    {
        await RecordingService.StartRecording(isBroadcasting);
    }

    protected async Task StopRecording()
    {
        await RecordingService.StopRecording();
    }
    #endregion

    #region Recording Event Subscriptions
    private void SubscribeToRecordingEvents()
    {
        RecordingService.OnRecordingStateChanged += HandleRecordingStateChanged;
    }

    private void UnsubscribeFromRecordingEvents()
    {
        if (RecordingService != null)
        {
            RecordingService.OnRecordingStateChanged -= HandleRecordingStateChanged;
        }
    }

    // Func<Task> Ÿ�Կ� �°� Task�� ��ȯ�ϵ��� ����
    private async Task HandleRecordingStateChanged()
    {
        await InvokeAsync(StateHasChanged);
    }
    #endregion

    #region Recording Audio Processing
    private void ProcessRecordingData(byte[] audioData)
    {
        if (isRecording && audioData != null && audioData.Length > 0)
        {
            RecordingService.AddAudioData(audioData);
        }
    }
    #endregion
}
