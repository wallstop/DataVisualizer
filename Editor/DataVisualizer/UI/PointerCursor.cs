namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using UnityEngine;

    public static class PointerCursor
    {
        public static UnityEngine.UIElements.Cursor Pointer
        {
            get
            {
                if (_texture == null)
                {
                    _texture = new Texture2D(
                        Shape[0].Length,
                        Shape.Length,
                        TextureFormat.RGBA32,
                        false
                    )
                    {
                        name = "DatavizPointerCursor",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    Color32 outline = new(0, 0, 0, 255);
                    Color32 fill = new(255, 255, 255, 255);
                    Color32[] pixels = new Color32[_texture.width * _texture.height];
                    for (int y = 0; y < Shape.Length; y++)
                    {
                        string row = Shape[y];
                        for (int x = 0; x < row.Length; x++)
                        {
                            pixels[y * _texture.width + x] = row[x] switch
                            {
                                'X' => outline,
                                'o' => fill,
                                _ => default,
                            };
                        }
                    }

                    _texture.SetPixels32(pixels);
                    _texture.Apply(false, false);
                }

                return new UnityEngine.UIElements.Cursor
                {
                    texture = _texture,
                    hotspot = new Vector2(5, 0),
                };
            }
        }

        private static readonly string[] Shape =
        {
            "....XX..........",
            "...XooX.........",
            "...XooX.........",
            "...XooX.........",
            "...XooX.........",
            "...XooX.........",
            "...XooXX........",
            "...XoooXX.......",
            "...XooooXX......",
            "...XoooooXX.....",
            "..XXoooooooX....",
            ".XoXXoooooooX...",
            ".XooXooooooooX..",
            ".XoXoooooooooX..",
            "..XooooooooooX..",
            "..XooooooooooX..",
            "..XooooooooooX..",
            "..XoooooooooX...",
            "..XoooooooooX...",
            "...XXXXXXXXX....",
        };

        private static Texture2D _texture;
    }
}
