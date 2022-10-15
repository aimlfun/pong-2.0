using System.Diagnostics;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace Pong
{
    /*
     * The video game Pong turns 50 this year (2022). 
     * The rules are simple. It's ping-pong, so you and another person each control a paddle on the screen hit a 
     * ball back and forth and try to avoid missing it for a higher score.
     * 
     * Pong and the Odyssey council were the first two games that were widely available for people to play when 
     * they came out in 1972. While other electronic games were available before Pong, it's often regarded as the 
     * first video game experience for people that involved a TV screen with a controller.
     */
    public partial class Pong : Form
    {
        /// <summary>
        /// This is the track bitmap. If null, the track image needs to be locked and copied to it.
        /// </summary>
        private static Bitmap? s_srcDisplayBitMap = null;

        /// <summary>
        /// This is the attributes of the track bitmap.
        /// </summary>
        private static BitmapData? s_srcDisplayMapData;

        /// <summary>
        /// This is a pointer to the track bitmap's data.
        /// </summary>
        private static IntPtr s_srcDisplayMapDataPtr;

        /// <summary>
        /// Bytes per row of pixels.
        /// </summary>
        private static int s_strideDisplay;

        /// <summary>
        /// This is how many bytes the track bitmap is.
        /// </summary>
        private static int s_totalLengthDisplay;

        /// <summary>
        /// This is the pixels in the track bitmap.
        /// </summary>
        internal static byte[] s_rgbValuesDisplay = Array.Empty<byte>();

        /// <summary>
        /// This is how many bytes each pixel occupies in the track bitmap.
        /// </summary>
        internal static int s_bytesPerPixelDisplay;

        /// <summary>
        /// This is how many bytes per row of the track bitmap image (used to multiply "y" by to get to the correct data).
        /// </summary>
        internal static int s_offsetDisplay;

        /// <summary>
        /// Height of s_canvas.
        /// </summary>
        private static readonly int height = 32;

        /// <summary>
        /// Template for display.
        /// </summary>
        internal static readonly Bitmap s_gameDisplayImageWithoutBat = new(32, 32);
        
        /// <summary>
        /// X (horizontal) position of the ball.
        /// </summary>
        float xBall = 0;
        
        /// <summary>
        /// Y (vertical) position of the ball.
        /// </summary>
        float yBall = 0;
        
        /// <summary>
        /// Horizontal ball speed.
        /// </summary>
        float dxBall = 0;
        
        /// <summary>
        /// Vertical ball speed.
        /// </summary>
        float dyBall = 1;

        /// <summary>
        /// The bat controlled by AI
        /// </summary>
        readonly AIBat aiControlledBat;

        /// <summary>
        /// The PictureBox we are rendering too.
        /// </summary>
        readonly PictureBox display;

        NeuralNetwork net;

        /// <summary>
        /// Constructor
        /// </summary>
        public Pong()
        {
            InitializeComponent();
        
            aiControlledBat = new AIBat(0);

            InitializeAI();

            Reset();

            display = pictureBox1;
            timer1.Start();
        }
        
        /// <summary>
        /// Teach AI to return the X pos for the bat based on the pixel illuminated.
        /// 
        /// To "hit" the ball, think of the problem as distilled into:
        ///   Bat Center X = Ball X.
        ///   
        /// Of course, we could code that, but it wouldn't be AI...
        /// 
        /// Let's "teach" the neural network to provide a bat position based on the dot position. 
        /// We do it by giving it a 32x32 array (1024) with a "dot" superimposed where a ball might 
        /// be. We then associate that dot with an X position.
        /// 
        /// Because the NN returns 0..1, we make it return 1/32 * xpos. 
        /// 
        /// To reverse 0..1 we multiply the "output" of the feed forward NN by 32.
        /// </summary>
        private void InitializeAI()
        {
            int[] layers = new int[2] { 1024, 1 };

            net = new(layers);

            if (net.LoadTrainedModel()) return;
            
            Dictionary<int, List<double[]>> trainingData = new();

            // do this lots of times to find the optimum weighting/biases.            
            for (int i = 0; i < 20000; i++)
            {
                if (i == 0) // first time, compute all the 1024 screens of 1024 (32X32 with 1 pixel)
                {
                    // video display is 32px * 32px
                    for (int ballX = 0; ballX < 32; ballX++)
                    {
                        if (!trainingData.ContainsKey(ballX)) trainingData.Add(ballX, new List<double[]>());

                        for (int ballY = 0; ballY < 32; ballY++)
                        {
                            double[] pixels = new double[1024]; // 32px * 32px = 1024.
                            pixels[ballX + ballY * 32] = 1; // where the ball is.

                            trainingData[ballX].Add(pixels);
                        }
                    }
                }

                bool trained = (i > 11000); // it's unlikely to be accurate before 11000 - approx 11,500 in testing for TanH

                // back propagate the data (give it the input plus expected output)
                foreach (int ballX in trainingData.Keys)
                {
                    foreach (double[] pixels in trainingData[ballX])
                    {
                        net.BackPropagate(pixels, new double[] { (float)ballX / 32 });

                        // test it afterwards, if it doesn't match we haven't trained it enough...

                        // This is where you get into the debate of pointless back propagation for zero gain vs. the overhead of testing something that isn't trained,
                        // so it doesn't attempt checking before 11,000.
                        
                        if (trained && (int)Math.Round(32F * net.FeedForward(pixels)[0]) != ballX) trained = false;
                    }
                }

                // if it's trained we can save doing it all 20000 times
                if (trained)
                {
                    net.SaveTrainedModel();
                    break; // no more training required, all 1024 permutations return the correct result.
                }
            }
        }
        
        /// <summary>
        /// Pick a random location to fire ball from, and provide a random speed in x and y direction.
        /// </summary>
        private void Reset()
        {
            yBall = 0;
 
            xBall = RandomNumberGenerator.GetInt32(0, 31);
            dxBall = (float)(RandomNumberGenerator.GetInt32(-100, 100)) / 50; // (-2F..2F)
            dyBall = 0.5F + ((float)(RandomNumberGenerator.GetInt32(0, 100)) / 200); /// 0.5F..1F
        }

        /// <summary>
        /// We need all 1024 pixels (32px * 32px) as an array to input into AI. This
        /// capture what is on screen, and turns it into a byte array.
        /// </summary>
        private static void CopyImageOfVideoDisplayToAnAccessibleInMemoryArray(Bitmap img)
        {
            if (img is null) throw new ArgumentNullException(nameof(img), "image should be populated before calling this."); // can't cache what has been drawn!

            s_srcDisplayBitMap = img;
            s_srcDisplayMapData = s_srcDisplayBitMap.LockBits(new Rectangle(0, 0, s_srcDisplayBitMap.Width, s_srcDisplayBitMap.Height), ImageLockMode.ReadOnly, img.PixelFormat);
            s_srcDisplayMapDataPtr = s_srcDisplayMapData.Scan0;
            s_strideDisplay = s_srcDisplayMapData.Stride;

            s_totalLengthDisplay = Math.Abs(s_strideDisplay) * s_srcDisplayBitMap.Height;
            s_rgbValuesDisplay = new byte[s_totalLengthDisplay];

            s_bytesPerPixelDisplay = Bitmap.GetPixelFormatSize(s_srcDisplayMapData.PixelFormat) / 8;
            s_offsetDisplay = s_strideDisplay;
            System.Runtime.InteropServices.Marshal.Copy(s_srcDisplayMapDataPtr, s_rgbValuesDisplay, 0, s_totalLengthDisplay);

            s_srcDisplayBitMap.UnlockBits(s_srcDisplayMapData);
        }

        /// <summary>
        /// Has the ball got to the bottom of the screen?
        /// </summary>
        /// <returns></returns>
        private bool BallHasReachedBaseLine()
        {
            return (Math.Round(yBall) >= (height - 2));
        }

        /// <summary>
        /// Move the ball, and check for end of game.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer1_Tick(object sender, EventArgs e)
        {         
            MoveBall();

            DrawBall();

            bool gameOver = BallHasReachedBaseLine();

            aiControlledBat.Move(net);
            
            DisplayDebug();

            // draw each AI bat and score it.
            aiControlledBat.Draw(display, gameOver);

            if (gameOver)
            {
                timer1.Stop();
                Application.DoEvents();
                Reset(); 
                timer1.Start();
            }
        }

        /// <summary>
        /// Output debug.
        /// </summary>
        private void DisplayDebug()
        {
            Bitmap img = new(pictureBoxDebug.Width, pictureBoxDebug.Height);
            
            using Graphics g = Graphics.FromImage(img);
            g.FillRectangle(Brushes.Black, 0, 0, img.Width, img.Height);    
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            StringBuilder sb = new (30);
            
            sb.AppendLine($"{RadarInAsciiArt(aiControlledBat.pixels, aiControlledBat.LeftPositionOfBat, aiControlledBat.RightPositionOfBat)}Neural Network Output: {aiControlledBat.result[0]:0.00}");
            
            using Font f = new("Lucida Console", 8);

            g.DrawString(sb.ToString(), f, Brushes.White, 1, 1);
            g.Flush();

            pictureBoxDebug.Image?.Dispose();
            pictureBoxDebug.Image = img;
        }

        /// <summary>
        /// ............ 
        /// or
        /// ...O........
        /// or
        /// 
        /// .....O.......=====..........
        ///      ^ ball    ^ bat
        /// or
        /// ...........==O==............
        ///              ^ ball 
        /// </summary>
        /// <param name="pixels"></param>
        /// <param name="l"></param>
        /// <param name="r"></param>
        /// <returns></returns>
        private static string RadarInAsciiArt(double[] pixels, int l, int r)
        {
            string result = "";

            bool found;

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    found = (pixels[x + y * 32] == 1);

                    result += (found ? "O" : (y == 31 && x >= l && x < r) ? "=" : ".");
                }

                result += "\n";
            }

            return result;
        }
        
        /// <summary>
        /// Draws the ball on the "video" screen, and stores the image for the NN to compute where to place the bat.
        /// </summary>
        private void DrawBall()
        {
            Graphics g = Graphics.FromImage(s_gameDisplayImageWithoutBat);

            g.Clear(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
            g.FillRectangle(Brushes.White, new RectangleF(xBall, yBall, 1, 1));
            g.Flush();

            CopyImageOfVideoDisplayToAnAccessibleInMemoryArray(s_gameDisplayImageWithoutBat);
        }
     
        /// <summary>
        /// Moves the ball.
        /// </summary>
        private void MoveBall()
        {
            xBall += dxBall;
            yBall += dyBall;

            if (xBall < 0 || xBall > 31 - Math.Abs(dxBall))
            {
                xBall -= dxBall;
                dxBall = -dxBall;
            }
        }

        /// <summary>
        /// "P" pauses, "S" slows it down
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            // "P" pauses the timer (and what's happening)
            if (e.KeyCode == Keys.P)
            {
                timer1.Enabled = !timer1.Enabled;
            }

            if (e.KeyCode == Keys.S)
            {
                timer1.Interval = timer1.Interval == 30 ? 600 : 30;
            }
        }
    }
}