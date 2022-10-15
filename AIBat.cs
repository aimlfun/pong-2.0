using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pong
{
    internal class AIBat
    {
        /// <summary>
        /// Width of the bat (drawn to left/right of center)
        /// </summary>
        private const int c_widthOfBatInPX = 5; //px

        /// <summary>
        /// Center X of the bat.
        /// </summary>
        internal double CenterPositionOfBat;

        /// <summary>
        /// Left edge of the bat.
        /// </summary>
        internal int LeftPositionOfBat
        {
            get
            {
                return (int)Math.Round(CenterPositionOfBat - c_widthOfBatInPX / 2);
            }
        }

        /// <summary>
        /// Right edge of the bat.
        /// </summary>
        internal int RightPositionOfBat
        {
            get
            {
                return (int)Math.Round(CenterPositionOfBat + 1+ c_widthOfBatInPX / 2);
            }
        }

        /// <summary>
        /// Where the AI wants the bat to be positioned
        /// </summary>
        private double desiredPositionOfBatX;

        /// <summary>
        /// Id of the bat.
        /// </summary>
        internal int Id;
       
        /// <summary>
        /// The AI output.
        /// </summary>
        internal double[] result;

        /// <summary>
        /// The monochrome pixels the AI sees.
        /// </summary>
        internal double[] pixels;

        /// <summary>
        /// Contructor.
        /// </summary>
        /// <param name="id"></param>
        internal AIBat(int id)
        {
            Id = id;
            CenterPositionOfBat = 16;
            desiredPositionOfBatX = CenterPositionOfBat;
            pixels = new double[32];
            result = new double[1];
        }

        /// <summary>
        /// Moves the bat to wherever the AI chooses.
        /// </summary>
        internal void Move(NeuralNetwork net)
        {
            result = AIviewFromAboveAndDecideResponse32x32pixels(net);

            // AI decides WHERE it wants the bat to be positioned (expressed as fraction of 32, 0..1, so we multiply by 32)
            desiredPositionOfBatX = Math.Round(result[0] * 32);

            // attempt to move towards the desired bat position, at max 3px per move
            CenterPositionOfBat = Clamp(desiredPositionOfBatX, CenterPositionOfBat - 3, CenterPositionOfBat + 3);

            // ensure the  bat doesn't go past the edges
            if (LeftPositionOfBat < 0) CenterPositionOfBat = c_widthOfBatInPX / 2;
            if (RightPositionOfBat > 31) CenterPositionOfBat = 31 - c_widthOfBatInPX / 2;
        }

        /// <summary>
        /// Returns ALL the pixels as a "double" array. The element containing a 1 has the ball.
        /// </summary>
        /// <returns></returns>
        private double[] AIviewFromAboveAndDecideResponse32x32pixels(NeuralNetwork net)
        {
            pixels = new double[1024];

            // convert 4 bytes per pixel to 1. (ARGB). Pixels are 255 R, 255 G, 255 B, 255 Alpha. We don't need to check all.
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Pong.s_rgbValuesDisplay[i * Pong.s_bytesPerPixelDisplay] != 0 ? 1 : 0; // 1 = ball (white pixel), 0 = no pixel.
            }

            return net.FeedForward(pixels); // [0] => new position for the bat. X = 0..31 encoded as X/32.
        }
        
        /// <summary>
        /// Draws the bat and ball.
        /// </summary>
        /// <param name="pictureBox"></param>
        /// <param name="label"></param>
        /// <param name="success"></param>
        internal void Draw(PictureBox pictureBox, bool ballHitBottomEdge = false)
        {
            Bitmap img = new(Pong.s_gameDisplayImageWithoutBat);

            // colours the background depending on state
            if (ballHitBottomEdge) img = ChangeColour(img, Color.LimeGreen);

            Graphics g = Graphics.FromImage(img);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

            g.DrawLine(Pens.White, LeftPositionOfBat, 31, RightPositionOfBat, 31);
            g.Flush();

            pictureBox.Image?.Dispose();
            pictureBox.Image = img;
        }

        /// <summary>
        /// Replace background with a different colour.
        /// </summary>
        /// <param name="inputBitmap"></param>
        /// <returns></returns>
        internal static Bitmap ChangeColour(Bitmap inputBitmap, Color colour)
        {
            Bitmap outputImage = new(inputBitmap.Width, inputBitmap.Height);
            using Graphics graphicsForReplacementBitmap = Graphics.FromImage(outputImage);
            graphicsForReplacementBitmap.DrawImage(inputBitmap, 0, 0);

            for (int y = 0; y < outputImage.Height; y++)
            {
                for (int x = 0; x < outputImage.Width; x++)
                {
                    Color PixelColor = outputImage.GetPixel(x, y);

                    // one would normally check R,G & B. But for our app, red channel is indicative. (R=0,G=0,B=0 = black)
                    if (PixelColor.R == 0) outputImage.SetPixel(x, y, colour);
                }
            }

            return outputImage;
        }

        /// <summary>
        /// Returns a value between min and max (never outside of).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="val"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        internal static double Clamp(double val, double min, double max)
        {
            if (val.CompareTo(min) < 0)
            {
                return min;
            }

            if (val.CompareTo(max) > 0)
            {
                return max;
            }

            return val;
        }
    }
}