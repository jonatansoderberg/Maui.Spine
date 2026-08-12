namespace Orientera.Services.Local;

/// <summary>
/// Whether the first-run welcome has been answered — either way.
/// </summary>
/// <remarks>
/// A file rather than "is the identity empty?", because skipping is an answer too and has to
/// survive a restart. Asking the same question every launch would turn a choice into nagging.
/// </remarks>
public sealed class FirstRunStore(string _path)
{
    public bool IsAnswered => File.Exists(_path);

    public void MarkAnswered()
    {
        try
        {
            File.WriteAllText(_path, DateTimeOffset.Now.ToString("o"));
        }
        catch (IOException)
        {
            // Worst case the welcome comes back next launch. Not worth failing a startup over.
        }
    }
}
