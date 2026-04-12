using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DeskPet;

/// <summary>
/// 负责跨进程管道的透明收发（双向流收发器）
/// </summary>
public class PetProcess : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event Action<IpcCommand>? InputReceived;
    public event Action<IpcEvent>? OutputReceived;

    public PetProcess(TextWriter writer, TextReader reader)
    {
        this.writer = writer;
        this.reader = reader;
    }

    public void SendInput(IpcCommand cmd)
    {
        Send(cmd);
    }

    public void SendOutput(IpcEvent ev)
    {
        Send(ev);
    }

    public void ListenInput()
    {
        StartListening(InputReceived);
    }

    public void ListenOutput()
    {
        StartListening(OutputReceived);
    }

    public void Dispose()
    {
        listeningCancellation?.Cancel();
    }

    void Send(object msg)
    {
        string json = JsonSerializer.Serialize(msg, JsonOptions);
        writer.WriteLine(json);
        writer.Flush();
    }

    async void StartListening<T>(Action<T>? callback)
    {
        try
        {
            SynchronizationContext? syncContext = SynchronizationContext.Current;

            listeningCancellation = new CancellationTokenSource();
            CancellationToken token = listeningCancellation.Token;
            await Task.Run(async () => {
                while (token.IsCancellationRequested == false)
                {
                    string? line = await reader.ReadLineAsync(token);
                    if (string.IsNullOrEmpty(line)) break;

                    T? msg = JsonSerializer.Deserialize<T>(line, JsonOptions);
                    if (msg == null) break;

                    if (syncContext != null)
                        syncContext.Post(_ => callback?.Invoke(msg), null);
                    else
                        callback?.Invoke(msg);

                    await File.AppendAllTextAsync("D:\\123.txt", line, token);
                }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            await File.AppendAllTextAsync("pet.log", e + Environment.NewLine);
        }
    }

    readonly TextWriter writer;
    readonly TextReader reader;
    CancellationTokenSource? listeningCancellation;
}

// --- IPC Protocol ---
[JsonDerivedType(typeof(WindowMoveCommand), "window-move")]
[JsonDerivedType(typeof(GetPositionCommand), "get-position")]
[JsonDerivedType(typeof(BubbleCommand), "bubble")]
[JsonDerivedType(typeof(PlayExpressionCommand), "expression")]
[JsonDerivedType(typeof(MotionCommand), "motion")]
[JsonDerivedType(typeof(HideBubbleCommand), "hide-bubble")]
public abstract record IpcCommand;

public record WindowMoveCommand(double X, double Y, int Duration) : IpcCommand;

public record GetPositionCommand : IpcCommand;

public record BubbleCommand(string Text) : IpcCommand;

public record PlayExpressionCommand(string? Id) : IpcCommand;

public record MotionCommand(string Group, int Index) : IpcCommand;

public record HideBubbleCommand : IpcCommand;

[JsonDerivedType(typeof(ReadyEvent), "ready")]
[JsonDerivedType(typeof(InputEvent), "input")]
[JsonDerivedType(typeof(InteractionEvent), "interaction")]
[JsonDerivedType(typeof(PositionEvent), "position")]
public abstract record IpcEvent;

public record ReadyEvent : IpcEvent;

public record InputEvent(string Text) : IpcEvent;

public record InteractionEvent(string Interaction) : IpcEvent;

public record PositionEvent(double X, double Y) : IpcEvent;
