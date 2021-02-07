using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;

namespace VideoToAscii
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string originalFilePath = "";
            string tempFolderPath = Directory.GetCurrentDirectory() + @"\temp\";
            string charMap = " .-+o#@";
            string prevFrame = "";
            string[] images;
            byte[,] consoleColours;
            ConsoleColor[] colours = (ConsoleColor[])ConsoleColor.GetValues(typeof(ConsoleColor));
            int frameRate;
            int width;
            int height;

            // inital text
            Console.Title = "Video To ASCII | Will Burland";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Press F11 to fullscreen, then enter to continue");
            Console.WriteLine("Additionally, move the mouse out of view in this window");
            Console.ReadLine();

            // output quality encoding selection
            int quality = -1;
            while (quality < 1 || quality > 51)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("/============QUALITY SETTINGS=============\\");
                Console.WriteLine("| 1  -> best quality, very high file size |");
                Console.WriteLine("| 51 -> worst quality, very low file size |");
                Console.WriteLine("\\=========================================/");
                Console.Write("\nEnter quality value from 1 to 51: ");
                
                try
                {
                    quality = int.Parse(Console.ReadLine());

                    if (quality < 1 || quality > 51)
                    {
                        Console.Clear();
                    }
                }
                catch
                {
                    Console.Clear();
                }
            }

            // user selecting file
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nChoose the video file to convert...");
            Thread.Sleep(1000);

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Video Files |*.avi;*.mp4;*.mv4;*.mov;*.wvm";
            openFileDialog.FilterIndex = 2;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                originalFilePath = openFileDialog.FileName;
            }
            else
            {
                Environment.Exit(0);
            }

            //main program
            Console.WriteLine("\nStarting in 3 seconds...");
            Thread.Sleep(3000);
            Console.Clear();

            // extract frames from source into /temp/
            File.Copy(originalFilePath, Directory.GetCurrentDirectory() + "\\" + Path.GetFileName(originalFilePath).Replace(" ", "_"));
            CreateTempFolder(tempFolderPath);
            ExtractFrames(Path.GetFileName(originalFilePath));
            File.Delete(Directory.GetCurrentDirectory() + "\\" + Path.GetFileName(originalFilePath).Replace(" ", "_"));

            width = Console.WindowWidth;
            height = Console.WindowHeight - 1;
            images = Directory.GetFiles(tempFolderPath, "*.png", SearchOption.TopDirectoryOnly);
            frameRate = GetFrameRate(originalFilePath);
            consoleColours = new byte[width, height];

            // convert each frame to ascii and save an image of the console
            for (int k = 0; k < images.Length; k++)
            {
                Bitmap image = new Bitmap(images[k]);
                Bitmap newImage = new Bitmap(image, new Size(width, height));

                Console.CursorVisible = false;
                Console.BufferWidth = width;
                Console.BufferHeight = height + 1;

                // update previous frame buffer
                prevFrame = "";
                foreach (string line in ConsoleReader.ReadFromBuffer(0, 0, (short)width, (short)height))
                {
                    prevFrame += line;
                }
                
                // for current frame
                for (int i = 0; i < height; i++)
                {
                    for (int j = 0; j < width; j++)
                    {
                        // get brightness of the current pixel and map it to charMap (ascii brightness scale)
                        Color pixel = newImage.GetPixel(j, i);

                        float brightness = (pixel.R + pixel.G + pixel.B) / 3;
                        char value = charMap[(int)Math.Floor((brightness / 256) * charMap.Length)];

                        for (int h = 0; h < colours.Length; h++)
                        {
                            ConsoleColor newColour = ToConsoleColor(ToHex(pixel));
                            if (colours[h] != newColour)
                            {
                                // only update if the ascii value changes, big perfomance save
                                if (prevFrame[i * width + j] != value)
                                {
                                    Console.SetCursorPosition(j, i);
                                    Console.ForegroundColor = ToConsoleColor(ToHex(pixel));
                                    Console.Write(value);
                                    consoleColours[j, i] = (byte)h;
                                    break;
                                }
                            }
                        }
                    }
                }
                Console.SetCursorPosition(0, 0);

                image.Dispose();
                newImage.Dispose();

                // save the output of the console (current frame as ascii) to png
                TakeScreenShot(tempFolderPath, k.ToString().PadLeft(8, '0'));
            }

            // create final video and show it in the explorer
            AssembleVideo(tempFolderPath, Path.GetFileName(originalFilePath).Replace(" ", "_"), frameRate, originalFilePath, quality);

            Process.Start("explorer.exe", "/select, \"" + Directory.GetCurrentDirectory() + "\\output.mp4");
        }

        public static int GetFrameRate(string path)
        {
            // get the frame rate of a video file
            ShellObject obj = ShellObject.FromParsingName(path);
            ShellProperty<uint?> rateProp = obj.Properties.GetProperty<uint?>("System.Video.FrameRate");
            return (int)(rateProp.Value / 1000);
        }

        public static void CreateTempFolder(string path)
        {
            // create an empty temp folder
            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true);
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        public static void ExtractFrames(string fileName)
        {
            // turn video file into images and move them to /temp/
            string path = Directory.GetCurrentDirectory() + @"\bin\ffmpeg.exe";
            string arguments = "-i " + fileName.Replace(" ", "_") + " %08d.png";

            Process.Start(path, arguments).WaitForExit();

            Thread.Sleep(5000);

            string[] images = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.png", SearchOption.TopDirectoryOnly);

            for (int i = 0; i < images.Length; i++)
            {
                File.Move(images[i], Directory.GetCurrentDirectory() + @"\temp\" + Path.GetFileName(images[i]));
            }
        }

        public static void AssembleVideo(string tempFolder, string fileName, int frameRate, string originalFilePath, int quality)
        {
            // combine frames to video, then add audio

            string directory = Directory.GetCurrentDirectory();
            string[] images = Directory.GetFiles(tempFolder, "*.png", SearchOption.TopDirectoryOnly);

            // move frames to main directory
            for (int i = 0; i < images.Length; i++)
            {
                File.Move(images[i], directory + "\\" + Path.GetFileName(images[i]));
            }
            Directory.Delete(tempFolder);

            images = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly);
            // delete last frame
            File.Delete(images[images.Length - 1]);

            // get size of the output video
            Bitmap temp = new Bitmap(images[0]);
            int width = temp.Width;
            int height = temp.Height;
            string size = width + "x" + height;
            temp.Dispose();

            // ffmpeg exe path
            string path = directory + @"\bin\ffmpeg.exe";

            // combine frames to avi video, with original framerate and quality options
            string arguments = "-framerate " + frameRate + " -pattern_type sequence -i \"%8d.png\" -y -pix_fmt yuv420p -color_trc smpte2084 -color_primaries bt2020 -vcodec libx264 -crf " + quality.ToString() + " -vsync vfr -s " + size + " video.avi";
            Process.Start(path, arguments).WaitForExit();

            // extract audio from original video
            File.Copy(originalFilePath, directory + "\\" + Path.GetFileName(originalFilePath).Replace(" ", "_"));
            arguments = "-i " + fileName + " -vn -acodec copy -y audio.aac";
            Process.Start(path, arguments).WaitForExit();

            // add audio to new ascii video as mp4
            arguments = "-i video.avi -i audio.aac -c:v copy -c:a aac -y output.mp4";
            Process.Start(path, arguments).WaitForExit();

            // remove leftover files
            File.Delete(directory + "\\" + "audio.aac");
            File.Delete(directory + "\\" + "video.avi");
            File.Delete(directory + "\\" + fileName.Replace(" ", "_"));

            // delete video frames
            foreach (string sFile in Directory.GetFiles(directory, "*.png"))
            {
                File.Delete(sFile);
            }
        }

        public static void TakeScreenShot(string tempFolderPath, string fileName)
        {
            // saves the output of the console as an image
            Rectangle bounds = Screen.GetBounds(Point.Empty);
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                }
                bitmap.Save(tempFolderPath + fileName + ".png", ImageFormat.Png);
            }
        }

        public static ConsoleColor ToConsoleColor(string hex)
        {
            // converts rgb colour to the closest ConsoleColor
            int argb = Int32.Parse(hex.Replace("#", ""), NumberStyles.HexNumber);
            Color c = Color.FromArgb(argb);

            int index = (c.R > 128 | c.G > 128 | c.B > 128) ? 8 : 0;
            index |= (c.R > 64) ? 4 : 0;
            index |= (c.G > 64) ? 2 : 0;
            index |= (c.B > 64) ? 1 : 0;

            return (System.ConsoleColor)index;
        }

        static String ToHex(Color c)
        {
            // Color object to rgb hex string
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

    }

    public class ConsoleReader
    {
        // class to get console output as array

        public static IEnumerable<string> ReadFromBuffer(short x, short y, short width, short height)
        {
            IntPtr buffer = Marshal.AllocHGlobal(width * height * Marshal.SizeOf(typeof(CHAR_INFO)));
            if (buffer == null)
                throw new OutOfMemoryException();

            try
            {
                COORD coord = new COORD();
                SMALL_RECT rc = new SMALL_RECT();
                rc.Left = x;
                rc.Top = y;
                rc.Right = (short)(x + width - 1);
                rc.Bottom = (short)(y + height - 1);

                COORD size = new COORD();
                size.X = width;
                size.Y = height;

                const int STD_OUTPUT_HANDLE = -11;
                if (!ReadConsoleOutput(GetStdHandle(STD_OUTPUT_HANDLE), buffer, size, coord, ref rc))
                {
                    // 'Not enough storage is available to process this command' may be raised for buffer size > 64K (see ReadConsoleOutput doc.)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                IntPtr ptr = buffer;
                for (int h = 0; h < height; h++)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int w = 0; w < width; w++)
                    {
                        CHAR_INFO ci = (CHAR_INFO)Marshal.PtrToStructure(ptr, typeof(CHAR_INFO));
                        char[] chars = Console.OutputEncoding.GetChars(ci.charData);
                        sb.Append(chars[0]);
                        ptr += Marshal.SizeOf(typeof(CHAR_INFO));
                    }
                    yield return sb.ToString();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CHAR_INFO
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] charData;
            public short attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public short wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadConsoleOutput(IntPtr hConsoleOutput, IntPtr lpBuffer, COORD dwBufferSize, COORD dwBufferCoord, ref SMALL_RECT lpReadRegion);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
    }
}
