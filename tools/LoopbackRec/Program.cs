using NAudio.Wave;

// Loopback capture of the default render device: records the game's sound effects without any
// system-side "Stereo Mix" device. Stops on Ctrl+C or when the stop file appears.
string outPath = args.Length > 0 ? args[0] : "loopback.wav";
string stopFile = args.Length > 1 ? args[1] : null;

using var capture = new WasapiLoopbackCapture();
var writer = new WaveFileWriter(outPath, capture.WaveFormat);
long bytes = 0;
capture.DataAvailable += (s, e) => { writer.Write(e.Buffer, 0, e.BytesRecorded); bytes += e.BytesRecorded; };
capture.RecordingStopped += (s, e) => { try { writer.Dispose(); } catch { } };
capture.StartRecording();
Console.WriteLine($"recording -> {outPath}  {capture.WaveFormat.SampleRate}Hz {capture.WaveFormat.Channels}ch");

while (true)
{
    if (stopFile != null && File.Exists(stopFile)) break;
    Thread.Sleep(100);
}
capture.StopRecording();
writer.Dispose();
Console.WriteLine($"bytes={bytes}");
