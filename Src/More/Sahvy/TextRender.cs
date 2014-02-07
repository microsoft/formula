using System;
using System.Drawing;
using System.Text;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace Sahvy
{
    public class TextRender
    {
        public TextRender(string text)
        {
            Font drawFont = new Font("Arial", 100, FontStyle.Bold);

            Bitmap tmp = new Bitmap(1, 1);
            using (Graphics gfx = Graphics.FromImage(tmp))
            {
                SizeF size = gfx.MeasureString(text, drawFont);

                width = 1;
                height = 1;
                while (width < (int)size.Width+1)
                    width *= 2;
                while (height < (int)size.Height+1)
                    height *= 2;
            }

            text_bmp = new Bitmap(width, height);
            text_texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, text_texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Linear);
            
            using (Graphics gfx = Graphics.FromImage(text_bmp))
            {
                gfx.Clear(Color.Transparent);
                SolidBrush drawBrush = new SolidBrush(Color.Black);
                StringFormat drawFormat = new StringFormat();
                drawFormat.Alignment = StringAlignment.Center;
                RectangleF drawRect = new RectangleF(0, 0, width, height);
                gfx.DrawString(text, drawFont, drawBrush, drawRect, drawFormat);
            }
            System.Drawing.Imaging.BitmapData data = 
                text_bmp.LockBits(new Rectangle(0, 0, text_bmp.Width, text_bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);

            text_bmp.UnlockBits(data);
        }

        Bitmap text_bmp;
        int text_texture;
        public int width { get; private set; }
        public int height { get; private set; }
        
        public void BindTexture()
        {
            GL.BindTexture(TextureTarget.Texture2D, text_texture);
        }

        public void Render3D(Vector3 loc, Vector3 right, Vector3 up)
        {
            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.Enable(EnableCap.Texture2D);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            BindTexture();
            GL.Begin(BeginMode.Quads);
            GL.Color4(Color4.White);
            GL.TexCoord2(0f, 1f); GL.Vertex3(loc);
            GL.TexCoord2(1f, 1f); GL.Vertex3(loc + right * width / 512f);
            GL.TexCoord2(1f, 0f); GL.Vertex3(loc + right * width / 512f + up * height / 512f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(loc + up * height / 512f);
            GL.End();
            GL.PopAttrib();
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
    }
}
