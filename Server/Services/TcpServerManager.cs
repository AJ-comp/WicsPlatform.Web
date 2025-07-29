using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WicsPlatform.Server.Services
{
    public interface ITcpServerManager
    {
        TcpClient GetClientByIP(string ip);
        bool IsClientConnected(string ip);
        Task SendToClient(string ip, byte[] data);
        Task SendToClients(List<string> ips, byte[] data);
    }

    public class TcpServerManager : BackgroundService, ITcpServerManager
    {
        private readonly ILogger<TcpServerManager> _logger;
        private readonly ConcurrentDictionary<string, TcpClient> _connectedClients = new();
        private TcpListener _tcpListener;
        private const int TCP_PORT = 5000;

        public TcpServerManager(ILogger<TcpServerManager> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await StartTcpServer(stoppingToken);
        }

        private async Task StartTcpServer(CancellationToken cancellationToken)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, TCP_PORT);
                _tcpListener.Start();
                _logger.LogInformation($"TCP 서버가 포트 {TCP_PORT}에서 시작되었습니다.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // 비동기적으로 클라이언트 연결 대기
                        var tcpClient = await AcceptClientAsync(cancellationToken);
                        if (tcpClient != null)
                        {
                            // 클라이언트 처리는 별도 태스크로
                            _ = Task.Run(() => HandleClient(tcpClient, cancellationToken), cancellationToken);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // 서버가 중지되는 경우
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TCP 서버 시작 중 오류 발생");
            }
            finally
            {
                _tcpListener?.Stop();
                _logger.LogInformation("TCP 서버가 중지되었습니다.");
            }
        }

        private async Task<TcpClient> AcceptClientAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (cancellationToken.Register(() => _tcpListener?.Stop()))
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    return tcpClient;
                }
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken cancellationToken)
        {
            var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            var clientIP = clientEndPoint?.Address.ToString();

            if (string.IsNullOrEmpty(clientIP))
            {
                client.Close();
                return;
            }

            try
            {
                // 기존 연결이 있으면 종료
                if (_connectedClients.TryRemove(clientIP, out var oldClient))
                {
                    oldClient.Close();
                    _logger.LogWarning($"기존 연결 종료: {clientIP}");
                }

                // 새 연결 등록
                _connectedClients[clientIP] = client;
                _logger.LogInformation($"스피커 연결됨: {clientIP}");

                // Keep-alive 설정
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // 연결 유지 (데이터 수신 대기)
                var buffer = new byte[1024];
                var stream = client.GetStream();

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0)
                        {
                            // 연결이 끊어짐
                            break;
                        }

                        // 스피커로부터 받은 데이터 처리 (상태 정보, 하트비트 등)
                        _logger.LogDebug($"스피커 {clientIP}로부터 {bytesRead} 바이트 수신");
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"클라이언트 처리 중 오류: {clientIP}");
            }
            finally
            {
                // 연결 종료 처리
                if (_connectedClients.TryRemove(clientIP, out _))
                {
                    _logger.LogInformation($"스피커 연결 해제: {clientIP}");
                }
                client.Close();
            }
        }

        public TcpClient GetClientByIP(string ip)
        {
            _connectedClients.TryGetValue(ip, out var client);
            return client;
        }

        public bool IsClientConnected(string ip)
        {
            if (_connectedClients.TryGetValue(ip, out var client))
            {
                return client.Connected;
            }
            return false;
        }

        public async Task SendToClient(string ip, byte[] data)
        {
            if (_connectedClients.TryGetValue(ip, out var client) && client.Connected)
            {
                try
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"데이터 전송 실패: {ip}");
                    _connectedClients.TryRemove(ip, out _);
                }
            }
        }

        public async Task SendToClients(List<string> ips, byte[] data)
        {
            var tasks = ips.Select(ip => SendToClient(ip, data));
            await Task.WhenAll(tasks);
        }

        public override void Dispose()
        {
            foreach (var client in _connectedClients.Values)
            {
                try { client.Close(); } catch { }
            }
            _connectedClients.Clear();
            _tcpListener?.Stop();
            base.Dispose();
        }
    }
}
