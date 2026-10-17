using System.Net.WebSockets;
using System.Text;

namespace Plugin.Maui.Spine.Voice;

/// <summary>A text WebSocket to the provider: whole messages in and out. Tests replace it with a scripted one.</summary>
internal interface IVoiceSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri url, string bearer, CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>The next whole message, or <see langword="null"/> when the provider closed the socket.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Why the provider closed the socket, if it said.</summary>
    string? CloseReason { get; }

    Task CloseAsync(CancellationToken cancellationToken);
}

internal sealed class ClientVoiceSocket : IVoiceSocket
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly byte[] _buffer = new byte[16 * 1024];

    public string? CloseReason { get; private set; }

    public Task ConnectAsync(Uri url, string bearer, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {bearer}");
        return _socket.ConnectAsync(url, cancellationToken);
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        // One message at a time: frames of two messages must not interleave.
        await _sending.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sending.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(_buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                CloseReason = _socket.CloseStatusDescription is { Length: > 0 } description
                    ? $"{_socket.CloseStatus}: {description}"
                    : _socket.CloseStatus?.ToString();
                return null;
            }

            message.Write(_buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "The conversation ended.", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _sending.Dispose();
        return ValueTask.CompletedTask;
    }
}
