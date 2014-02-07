using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using OpenTK.Input;

namespace Sahvy
{
    public delegate void DrawHandler(object sender, EventArgs e);

    public class Plot3d : GameWindow
    {
        public Plot3d()
            : base(1280, 960, new GraphicsMode(new ColorFormat(32), 24, 0, 2), "Plot 3D flowpipe")
        {
            VSync = VSyncMode.On;
        }

        public Object plotDataLock = new Object();                
        public event DrawHandler DrawEvent;

        public Dictionary<string, TextRender> texts = new Dictionary<string,TextRender>();
        public void BindText(string text)
        {
            TextRender tex;
            if (texts.TryGetValue(text, out tex))
            {
                tex.BindTexture();
                return;
            }
            else
            {
                tex = new TextRender(text);
                texts.Add(text, tex);
                tex.BindTexture();
            }
        }
        public void RenderText(string text, Vector3 loc, Vector3 right, Vector3 up)
        {
            if (text == null)
                text = "(null)";
            TextRender tex;
            if (texts.TryGetValue(text, out tex))
            {
                tex.Render3D(loc,right,up);
                return;
            }
            else
            {
                tex = new TextRender(text);
                texts.Add(text, tex);
                tex.Render3D(loc, right, up);
            }
        }
        

        private void DrawGrid(double minx, double maxx, double miny, double maxy, double stepx, double stepy)
        {
            GL.Begin(BeginMode.Lines);
            GL.Color4(OpenTK.Graphics.Color4.Black);
            for (double x = minx; x <= maxx; x += stepx)
            {
                GL.Vertex3(x, miny, 0);
                GL.Vertex3(x, maxy, 0);
            }
            for (double y = miny; y <= maxy; y += stepy)
            {
                GL.Vertex3(minx, y, 0);
                GL.Vertex3(maxx, y, 0);
            }
            GL.End();
        }
        private double minx, maxx, miny, maxy, minz, maxz;
        public void SetMinMax(double minx, double maxx, double miny, double maxy, double minz, double maxz)
        {
            this.minx = minx;
            this.miny = miny;
            this.minz = minz;
            this.maxx = maxx;
            this.maxy = maxy;
            this.maxz = maxz;
        }
        public void DrawGrids(int divs)
        {
            double stepx = Math.Max((maxx - minx) / divs, 1e-5);
            double stepy = Math.Max((maxy - miny) / divs, 1e-5);
            double stepz = Math.Max((maxz - minz) / divs, 1e-5);
            Matrix4d m;
            GL.MatrixMode(MatrixMode.Modelview);
            
            // fix Z        
            if (camera.Z > minz)
            {
                GL.PushMatrix();
                GL.Translate(0, 0, minz);
                DrawGrid(minx, maxx, miny, maxy, stepx, stepy);
                GL.PopMatrix();
            }

            if (camera.Z < maxz)
            {
                GL.PushMatrix();
                GL.Translate(0, 0, maxz);
                DrawGrid(minx, maxx, miny, maxy, stepx, stepy);
                GL.PopMatrix();
            }
            
            // fix X
            if (camera.X > minx)
            {
                m = new Matrix4d();
                m.M12 = 1; m.M23 = 1; m.M41 = (float)minx; m.M44 = 1;
                GL.PushMatrix();
                GL.MultMatrix(ref m);
                DrawGrid(miny, maxy, minz, maxz, stepy, stepz);
                GL.PopMatrix();
            }

            if (camera.X < maxx)
            {
                m = new Matrix4d();
                m.M12 = 1; m.M23 = 1; m.M41 = (float)maxx; m.M44 = 1;
                GL.PushMatrix();
                GL.MultMatrix(ref m);
                DrawGrid(miny, maxy, minz, maxz, stepy, stepz);
                GL.PopMatrix();
            }

            // fix Y
            if (camera.Y > miny)
            {
                m = new Matrix4d();
                m.M11 = 1; m.M23 = 1; m.M42 = (float)miny; m.M44 = 1;
                GL.PushMatrix();
                GL.MultMatrix(ref m);
                DrawGrid(minx, maxx, minz, maxz, stepx, stepz);
                GL.PopMatrix();
            }

            if (camera.Y < maxy)
            {
                m = new Matrix4d();
                m.M11 = 1; m.M23 = 1; m.M42 = (float)maxy; m.M44 = 1;
                GL.PushMatrix();
                GL.MultMatrix(ref m);
                DrawGrid(minx, maxx, minz, maxz, stepx, stepz);
                GL.PopMatrix();
            }
        }

        public void DrawAxisHelp(string[] axisName)
        {
            if (Is2DProjection) return;
            Vector3 zero = camera + LookAtPoint*10 + CameraLeft * 4.5f + CameraUpwards * 2.5f;
            Vector3 axisX = zero + new Vector3(1, 0, 0);
            Vector3 axisY = zero + new Vector3(0, 1, 0);
            Vector3 axisZ = zero + new Vector3(0, 0, 1);
            GL.Begin(BeginMode.Lines);
            GL.Color4(Color4.Brown);  GL.Vertex3(zero); GL.Vertex3(axisX);
            GL.Color4(Color4.Green);  GL.Vertex3(zero); GL.Vertex3(axisY);
            GL.Color4(Color4.Blue);   GL.Vertex3(zero); GL.Vertex3(axisZ);
            GL.End();
            
            RenderText(axisName[0], axisX, Vector3.UnitX, Vector3.UnitY);
            RenderText(axisName[1], axisY, Vector3.UnitX, Vector3.UnitY);
            RenderText(axisName[2], axisZ, Vector3.UnitX, -Vector3.UnitZ);
        }

        public void DefaultSettings()
        {
            float scale = (float)Math.Max(Math.Min(maxz-minz, maxy-miny), maxx-minx) / 2f;
            camera = new OpenTK.Vector3(-0.4f, 0, 1) * scale + new Vector3((float)minx, (float)maxy, (float)minz);
            Yaw = -0.73;
            Pitch = -0.32;
            MoveSpeed = scale;
            clearColor = new OpenTK.Graphics.Color4(1.0f, 1, 1, 1);
        }
        
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            WindowCenter = new Point(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);

            GL.ClearColor(clearColor);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Texture2D);
            GL.Disable(EnableCap.CullFace);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            Mouse.Move += new EventHandler<MouseMoveEventArgs>(OnMouseMove);
            Mouse.ButtonDown += new EventHandler<MouseButtonEventArgs>(OnMouseButtonDown);
            Mouse.ButtonUp += new EventHandler<MouseButtonEventArgs>(OnMouseButtonUp);
        }

        public Color4 clearColor = new Color4(0,0,0,1.0f);
        public float MoveSpeed = 50.0f;
        private Matrix4 CameraMatrix = Matrix4.Identity;
        private Matrix4 projection;
        private Vector2 MouseDelta;
        private Vector2 Mousecamera;
        public double Pitch = 0.0f;
        public double Yaw = 0.0f;
        private Point WindowCenter;
        private Vector2 MouseButtonPressedAt;
        private double YawBefore;
        private double PitchBefore;
        private bool MouseButtonPressed;
        public Vector3 LookAtPoint;
        private Vector3 CameraLeft;
        private Vector3 CameraUpwards;
        private bool Is2DProjection = false;

        void OnMouseMove(object sender, MouseMoveEventArgs e)
        {
            Mousecamera = new Vector2(e.X, e.Y);
        }
        void OnMouseButtonDown(object sender, MouseButtonEventArgs e)
        {
            MouseButtonPressedAt = Mousecamera;
            MouseButtonPressed = true;
            YawBefore = Yaw;
            PitchBefore = Pitch;
        }
        void OnMouseButtonUp(object sender, MouseButtonEventArgs e)
        {
            MouseButtonPressed = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            GL.Viewport(ClientRectangle.X, ClientRectangle.Y, ClientRectangle.Width, ClientRectangle.Height);

            projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 4, Width / (float)Height, 0.01f, 1000.0f);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projection);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            if (Keyboard[Key.W])
            {
                camera.X += (float)Math.Cos(Yaw) * MoveSpeed * (float)e.Time;
                camera.Y += (float)Math.Tan(Pitch) * MoveSpeed * (float)e.Time;
                camera.Z += (float)Math.Sin(Yaw) * MoveSpeed * (float)e.Time;
            }
            if (Keyboard[Key.S])
            {
                camera.X -= (float)Math.Cos(Yaw) * MoveSpeed * (float)e.Time;
                camera.Y -= (float)Math.Tan(Pitch) * MoveSpeed * (float)e.Time;
                camera.Z -= (float)Math.Sin(Yaw) * MoveSpeed * (float)e.Time;
            }
            if (Keyboard[Key.A])
            {
                camera.X -= (float)Math.Cos(Yaw + Math.PI / 2) * MoveSpeed * (float)e.Time;
                camera.Z -= (float)Math.Sin(Yaw + Math.PI / 2) * MoveSpeed * (float)e.Time;
            }
            if (Keyboard[Key.D])
            {
                camera.X += (float)Math.Cos(Yaw + Math.PI / 2) * MoveSpeed * (float)e.Time;
                camera.Z += (float)Math.Sin(Yaw + Math.PI / 2) * MoveSpeed * (float)e.Time;
            }
            if (Keyboard[Key.Space])
            {
                camera.Y += MoveSpeed * (float)e.Time;
            }            
            if (Keyboard[Key.LShift] || Keyboard[Key.RShift])
            {
                camera.Y -= MoveSpeed * (float)e.Time;
            }
            if (Keyboard[Key.H])
            {
                Console.WriteLine("Camera position: {0}", camera);
                Console.WriteLine("Yaw: {0}", Yaw);
                Console.WriteLine("Pitch: {0}", Pitch);
            }
            // a quick hack for 2d projection
            if (Keyboard[Key.Z])
            {
                Is2DProjection = true;
                Matrix4 proj = Matrix4.CreateOrthographic((float)maxx-(float)minx + 10f, (float)maxy-(float)miny + 10f, 0.01f, (float)maxz - (float)minz + 1000f);
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadMatrix(ref proj);
                camera = new Vector3((float)(minx + maxx) / 2f, (float)(miny + maxy) / 2f, (float)maxz + 50f);
                Yaw = -Math.PI / 2;
                Pitch = 0;
            }
            if (Keyboard[Key.X])
            {
                Is2DProjection = true;
                Matrix4 proj = Matrix4.CreateOrthographic((float)maxz - (float)minz + 10f, (float)maxy - (float)miny + 10f, 0.01f, (float)maxx - (float)minx + 1000f);
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadMatrix(ref proj);
                camera = new Vector3((float)minx - 50, (float)(miny + maxy) / 2f, (float)(minz + maxz) / 2f);
                Yaw = 0;
                Pitch = 0;
            }
            if (Keyboard[Key.R])
            {                
                Is2DProjection = false;
                DefaultSettings();
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadMatrix(ref projection);
            }
            if (MouseButtonPressed)
            {
                MouseDelta = Mousecamera - MouseButtonPressedAt;

                Yaw = YawBefore + MouseDelta.X / 200.0f;
                Pitch = PitchBefore - MouseDelta.Y / 200.0f;

                if (Pitch < -Math.PI/2 + 0.1f)
                    Pitch = -Math.PI/2 + 0.1f;
                if (Pitch > Math.PI/2 - 0.1f)
                    Pitch = Math.PI/2 - 0.1f;
            }

            LookAtPoint = new Vector3((float)Math.Cos(Yaw), (float)Math.Tan(Pitch), (float)Math.Sin(Yaw));
            LookAtPoint.Normalize();
            CameraMatrix = Matrix4.LookAt(camera, camera + LookAtPoint, Vector3.UnitY);
            CameraLeft = Vector3.Cross(Vector3.UnitY, LookAtPoint);
            CameraLeft.Normalize();
            CameraUpwards = Vector3.Cross(LookAtPoint, CameraLeft);
            CameraUpwards.Normalize();

            if (Keyboard[Key.Escape])
                Exit();
        }
        
        public Vector3 camera = new Vector3(0, 0, 400);
        public Vector3 target = new Vector3(0, 0, -400);
        public Vector3 up = Vector3.UnitY;

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            GL.ClearColor(clearColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                        
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref CameraMatrix);

            lock (plotDataLock)
            {                
                if (DrawEvent != null)
                    DrawEvent(this, new EventArgs());
            }                        

            SwapBuffers();
        }
    }
}