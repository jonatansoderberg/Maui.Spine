namespace Plugin.Maui.SvgIcon;

internal static class IcoWriter
{
    public static byte[] Build(List<(int Size, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)images.Count);

        int offset = 6 + (16 * images.Count);

        foreach (var img in images)
        {
            bw.Write((byte)(img.Size >= 256 ? 0 : img.Size));
            bw.Write((byte)(img.Size >= 256 ? 0 : img.Size));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(img.Png.Length);
            bw.Write(offset);

            offset += img.Png.Length;
        }

        foreach (var img in images)
            bw.Write(img.Png);

        return ms.ToArray();
    }
}