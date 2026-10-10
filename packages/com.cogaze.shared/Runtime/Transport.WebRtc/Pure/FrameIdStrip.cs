namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// TBD-04 (provisional, docs/decisions/TBD-04.md): the video_frame_id travels inside the picture, as
    /// a strip of black and white cells below the camera image, so the expert can read back exactly
    /// which frame its texture holds. Unity WebRTC gives C# neither the RTP timestamp of a decoded
    /// frame nor a hook between "this texture" and "that encoded frame", so the pixels are the only
    /// carrier that cannot fall out of step with the image.
    ///
    /// Layout, left to right, one cell = CellWidth(width) px wide and the full strip high:
    /// 4 marker cells (white, black, white, black) that set the threshold, 32 id bits MSB first,
    /// 8 bits CRC-8 of the id. Cells are wide and the strip is a whole 16-px macroblock row so the
    /// pattern survives H.264 at ordinary bitrates. Pixels are RGBA32/BGRA32 bytes, grey, so the
    /// channel order does not matter. Pure: the GPU side only copies bytes in and out.
    /// </summary>
    public static class FrameIdStrip
    {
        public const int HeightPx = 16;
        const int MarkerCells = 4;
        const int IdBits = 32;
        const int CheckBits = 8;
        public const int Cells = MarkerCells + IdBits + CheckBits;
        /// <summary>Marker white minus marker black, in 0..255 grey levels, below which the strip is not trusted.</summary>
        const float MinContrast = 64f;

        public static int CellWidth(int widthPx) => widthPx / Cells;

        /// <summary>False when the image is too narrow for cells that survive compression.</summary>
        public static bool Fits(int widthPx) => CellWidth(widthPx) >= 4;

        /// <summary>Fills a widthPx x HeightPx RGBA buffer (4 bytes per pixel) with the strip for this id.</summary>
        public static void Paint(uint id, byte[] rgba, int widthPx)
        {
            var bits = Bits(id);
            int cw = CellWidth(widthPx);
            for (int y = 0; y < HeightPx; y++)
                for (int x = 0; x < widthPx; x++)
                {
                    int cell = x / cw;
                    byte v = cell < Cells && bits[cell] ? (byte)255 : (byte)0;
                    int i = (y * widthPx + x) * 4;
                    rgba[i] = v;
                    rgba[i + 1] = v;
                    rgba[i + 2] = v;
                    rgba[i + 3] = 255;
                }
        }

        /// <summary>
        /// Reads the id from a widthPx x heightPx RGBA buffer holding the strip. Uses only the centre
        /// of each cell and of the strip's height, where compression bleeds least. False when the
        /// marker has too little contrast or the check bits disagree.
        /// </summary>
        public static bool TryRead(byte[] rgba, int widthPx, int heightPx, out uint id)
        {
            id = 0;
            int cw = CellWidth(widthPx);
            if (cw < 4 || heightPx < 4 || rgba == null || rgba.Length < widthPx * heightPx * 4) return false;

            var grey = new float[Cells];
            for (int c = 0; c < Cells; c++) grey[c] = CellGrey(rgba, widthPx, heightPx, c * cw + cw / 4, c * cw + cw * 3 / 4);

            float white = (grey[0] + grey[2]) / 2f, black = (grey[1] + grey[3]) / 2f;
            if (white - black < MinContrast) return false;
            float threshold = (white + black) / 2f;

            uint value = 0;
            for (int b = 0; b < IdBits; b++) value = (value << 1) | (grey[MarkerCells + b] > threshold ? 1u : 0u);
            int check = 0;
            for (int b = 0; b < CheckBits; b++) check = (check << 1) | (grey[MarkerCells + IdBits + b] > threshold ? 1 : 0);
            if (check != Crc8(value)) return false;
            id = value;
            return true;
        }

        static float CellGrey(byte[] rgba, int widthPx, int heightPx, int x0, int x1)
        {
            int y0 = heightPx / 4, y1 = heightPx * 3 / 4;
            long sum = 0;
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = (y * widthPx + x) * 4;
                    sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
                    n += 3;
                }
            return n > 0 ? (float)sum / n : 0f;
        }

        static bool[] Bits(uint id)
        {
            var bits = new bool[Cells];
            bits[0] = true;
            bits[2] = true;
            for (int b = 0; b < IdBits; b++) bits[MarkerCells + b] = ((id >> (IdBits - 1 - b)) & 1) != 0;
            int crc = Crc8(id);
            for (int b = 0; b < CheckBits; b++) bits[MarkerCells + IdBits + b] = ((crc >> (CheckBits - 1 - b)) & 1) != 0;
            return bits;
        }

        /// <summary>CRC-8 (polynomial 0x07) over the id's four bytes, big-endian.</summary>
        static int Crc8(uint id)
        {
            int crc = 0;
            for (int k = 3; k >= 0; k--)
            {
                crc ^= (int)((id >> (8 * k)) & 0xFF);
                for (int i = 0; i < 8; i++) crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
            return crc;
        }
    }
}
