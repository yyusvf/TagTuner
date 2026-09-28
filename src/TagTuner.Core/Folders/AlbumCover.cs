using System.Security.Cryptography;
using TagTuner.Core.Audio;
using TagTuner.Core.Model;

namespace TagTuner.Core.Folders;

/// <summary>
/// Das Cover eines Albums für den Album-Modus: das, was die meisten Dateien
/// tragen. Dazu die Dateien, bei denen es fehlt oder ein anderes ist.
///
/// Stand bis zur Mac-App nur in der Windows-Oberfläche; hier, damit beide
/// dieselbe Mehrheit bilden.
/// </summary>
public static class AlbumCover
{
    public static (AudioProbe.Cover? Cover, HashSet<string> Needs) Majority(IReadOnlyList<AudioTrack> tracks) =>
        Majority(tracks, t => AudioProbe.ReadCover(t.Path));

    /// <param name="read">Liest das Cover einer Datei. Eigens übergeben, damit es sich ohne Dateien prüfen lässt.</param>
    public static (AudioProbe.Cover? Cover, HashSet<string> Needs) Majority(
        IReadOnlyList<AudioTrack> tracks, Func<AudioTrack, AudioProbe.Cover?> read)
    {
        var byFile = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var samples = new Dictionary<string, AudioProbe.Cover>();

        foreach (var track in tracks)
        {
            string? hash = null;
            if (track.HasCover)
            {
                try
                {
                    if (read(track) is { Data.Length: > 0 } c)
                    {
                        hash = Convert.ToHexString(SHA256.HashData(c.Data));
                        samples.TryAdd(hash, c);
                    }
                }
                catch { }
            }
            byFile[track.Path] = hash;
        }

        var winner = byFile.Values.OfType<string>()
                           .GroupBy(h => h)
                           .OrderByDescending(g => g.Count())
                           .Select(g => g.Key)
                           .FirstOrDefault();

        var needs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (winner is null) return (null, needs);

        foreach (var (path, hash) in byFile)
            if (hash != winner) needs.Add(path);

        return (samples[winner], needs);
    }
}
