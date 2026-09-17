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
                    Color32 fill = new(255, 255, 255, 255);
                    Color32[] pixels = new Color32[_texture.width * _texture.height];
                    for (int y = 0; y < Shape.Length; y++)
                    {
                        string row = Shape[y];
                        for (int x = 0; x < row.Length; x++)
                        {
                            pixels[y * _texture.width + x] = row[x] == 'X' ? fill : default;
                        }
                    }

                    _texture.SetPixels32(pixels);
                    _texture.Apply(false, false);
                }

                return new UnityEngine.UIElements.Cursor
                {
                    texture = _texture,
                    hotspot = new Vector2(0, 0),
                };
            }
        }

        private static readonly string[] Shape =
        {
            "X............",
            "XX...........",
            "X.X..........",
            "X..X.........",
            "X...X........",
            "X....X.......",
            "X.....X......",
            "X......X.....",
            "X.......X....",
            "X........X...",
            "X.....XX.....",
            "X..X..X......",
            "X.X.X........",
            "XX..X........",
            "....X........",
        };

        private static Texture2D _texture;
    }
}
