using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace AdvancedScreensaver
{
    public class Particle
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector4 Color { get; set; }
        public float Size { get; set; }
        public float Life { get; set; }
    }

    public class Sphere
    {
        private int _vao;
        private int _vbo;
        private int _vertexCount;

        public Sphere(float radius, int segments)
        {
            var vertices = new List<Vector3>();

            for (int i = 0; i <= segments; i++)
            {
                double lat = Math.PI * i / segments;
                for (int j = 0; j <= segments; j++)
                {
                    double lon = 2 * Math.PI * j / segments;
                    float x = radius * (float)(Math.Sin(lat) * Math.Cos(lon));
                    float y = radius * (float)(Math.Sin(lat) * Math.Sin(lon));
                    float z = radius * (float)Math.Cos(lat);
                    vertices.Add(new Vector3(x, y, z));
                }
            }

            _vertexCount = vertices.Count;

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * Vector3.SizeInBytes, vertices.ToArray(), BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);

            GL.BindVertexArray(0);
        }

        public void Draw()
        {
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, _vertexCount);
            GL.BindVertexArray(0);
        }
    }

    public class Camera
    {
        public Vector3 Position { get; set; }
        public Vector3 Front { get; private set; } = -Vector3.UnitZ;
        public Vector3 Up { get; private set; } = Vector3.UnitY;
        public Vector3 Right { get; private set; } = Vector3.UnitX;

        private float _pitch;
        private float _yaw = -90f;
        public float AspectRatio { get; set; }

        public Camera(Vector3 position, float aspectRatio)
        {
            Position = position;
            AspectRatio = aspectRatio;
            UpdateVectors();
        }

        public float Pitch
        {
            get => _pitch;
            set
            {
                _pitch = MathHelper.Clamp(value, -89f, 89f);
                UpdateVectors();
            }
        }

        public float Yaw
        {
            get => _yaw;
            set
            {
                _yaw = value;
                UpdateVectors();
            }
        }

        public Matrix4 GetViewMatrix()
        {
            return Matrix4.LookAt(Position, Position + Front, Up);
        }

        public Matrix4 GetProjectionMatrix()
        {
            return Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), AspectRatio, 0.1f, 100f);
        }

        private void UpdateVectors()
        {
            Vector3 _Front;
            _Front.X = (float)Math.Cos(MathHelper.DegreesToRadians(Pitch)) *
                     (float)Math.Cos(MathHelper.DegreesToRadians(Yaw));
            _Front.Y = (float)Math.Sin(MathHelper.DegreesToRadians(Pitch));
            _Front.Z = (float)Math.Cos(MathHelper.DegreesToRadians(Pitch)) *
                     (float)Math.Sin(MathHelper.DegreesToRadians(Yaw));

            _Front = Vector3.Normalize(_Front);
            Front = _Front;

            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }
    }

    public class Screensaver : GameWindow
    {
        private const int ParticleCount = 10000;
        private List<Particle> _particles;
        private Random _random;

        private int _vertexArrayObject;
        private int _vertexBufferObject;
        private int _shaderProgram;
        private int _texture;

        private float _time;
        private Camera _camera;
        private Sphere _whiteSphere;
        private Sphere _brownSphere;
        private int _sphereShader;
        bool CursorVisible;

        private Vector2 _lastMousePosition;
        private bool _mouseMoved;


        public Screensaver()
            : base(GameWindowSettings.Default,
                  new NativeWindowSettings()
                  {
                      Size = new Vector2i(1280, 720),
                      Title = "Advanced Screensaver",
                      WindowBorder = WindowBorder.Hidden,
                      WindowState = WindowState.Fullscreen,
                      StartVisible = true,
                      APIVersion = new Version(4, 1)
                  })
        {
            _random = new Random();
            _particles = new List<Particle>();
            _camera = new Camera(Vector3.UnitZ * 10, ClientSize.X / (float)ClientSize.Y);
            _lastMousePosition = new Vector2(0, 0);
            _mouseMoved = false;
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            GL.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.PointSprite);
            GL.Enable(EnableCap.ProgramPointSize);

            CursorVisible = false;
            CursorState = CursorState.Grabbed; // Курсор скрыт и зафиксирован в центре

            InitializeParticles();
            SetupShaders();
            SetupTexture();

            // Инициализация сфер
            _whiteSphere = new Sphere(5.0f, 32);
            _brownSphere = new Sphere(4.8f, 32);

            // Шейдер для сфер
            string sphereVertShader = @"#version 410 core
                layout(location = 0) in vec3 aPosition;
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;
                void main()
                {
                    gl_Position = projection * view * model * vec4(aPosition, 1.0);
                }";

            string sphereFragShader = @"#version 410 core
                out vec4 fragColor;
                uniform vec4 color;
                void main()
                {
                    fragColor = color;
                }";

            _sphereShader = CompileShader(sphereVertShader, sphereFragShader);

            CursorVisible = false;
        }

        private int CompileShader(string vertSource, string fragSource)
        {
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertSource);
            GL.CompileShader(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragSource);
            GL.CompileShader(fragmentShader);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);

            GL.DetachShader(program, vertexShader);
            GL.DetachShader(program, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return program;
        }

        private void InitializeParticles()
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                _particles.Add(CreateParticle());
            }
        }

        private Particle CreateParticle()
        {
            var position = new Vector3(
                (float)(_random.NextDouble() * 2 - 1) * 10,
                (float)(_random.NextDouble() * 2 - 1) * 10,
                (float)(_random.NextDouble() * 2 - 1) * 10);

            var velocity = new Vector3(
                (float)(_random.NextDouble() * 2 - 1) * 0.01f,
                (float)(_random.NextDouble() * 2 - 1) * 0.01f,
                (float)(_random.NextDouble() * 2 - 1) * 0.01f);

            var color = new Vector4(
                (float)_random.NextDouble(),
                (float)_random.NextDouble(),
                (float)_random.NextDouble(),
                0.8f + (float)_random.NextDouble() * 0.2f);

            return new Particle
            {
                Position = position,
                Velocity = velocity,
                Color = color,
                Size = 5 + (float)_random.NextDouble() * 15,
                Life = 100 + (float)_random.NextDouble() * 900
            };
        }

        private void SetupShaders()
        {
            string vertexShaderSource = @"#version 410 core
                layout(location = 0) in vec3 aPosition;
                layout(location = 1) in vec4 aColor;
                layout(location = 2) in float aSize;
                
                out vec4 vColor;
                out float vSize;
                
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;
                
                void main()
                {
                    gl_Position = projection * view * model * vec4(aPosition, 1.0);
                    vColor = aColor;
                    vSize = aSize;
                    gl_PointSize = aSize;
                }";

            string fragmentShaderSource = @"#version 410 core
                in vec4 vColor;
                in float vSize;
                
                out vec4 fragColor;
                
                uniform sampler2D particleTexture;
                
                void main()
                {
                    vec2 coord = gl_PointCoord - vec2(0.5);
                    if (length(coord) > 0.5) discard;
                    
                    vec4 texColor = texture(particleTexture, gl_PointCoord);
                    fragColor = vColor * texColor;
                    fragColor.a *= smoothstep(0.5, 0.4, length(coord));
                }";

            _shaderProgram = CompileShader(vertexShaderSource, fragmentShaderSource);

            _vertexArrayObject = GL.GenVertexArray();
            _vertexBufferObject = GL.GenBuffer();

            GL.BindVertexArray(_vertexArrayObject);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 32, 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 32, 12);

            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 32, 28);

            GL.BindVertexArray(0);
        }

        private void SetupTexture()
        {
            _texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _texture);

            byte[] textureData = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int index = (y * 64 + x) * 4;
                    float dx = (x - 32) / 32.0f;
                    float dy = (y - 32) / 32.0f;
                    float dist = Math.Min(1.0f, (float)Math.Sqrt(dx * dx + dy * dy));
                    byte alpha = (byte)(255 * (1.0f - dist * dist));

                    textureData[index] = 255;
                    textureData[index + 1] = 255;
                    textureData[index + 2] = 255;
                    textureData[index + 3] = alpha;
                }
            }

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 64, 64, 0,
                         PixelFormat.Rgba, PixelType.UnsignedByte, textureData);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Рендеринг сфер
            GL.UseProgram(_sphereShader);

            // Белая сфера
            Matrix4 sphereModel = Matrix4.CreateTranslation(0, 0, 0);
            Matrix4 sphereView = _camera.GetViewMatrix();
            Matrix4 sphereProjection = _camera.GetProjectionMatrix();

            GL.UniformMatrix4(GL.GetUniformLocation(_sphereShader, "model"), false, ref sphereModel);
            GL.UniformMatrix4(GL.GetUniformLocation(_sphereShader, "view"), false, ref sphereView);
            GL.UniformMatrix4(GL.GetUniformLocation(_sphereShader, "projection"), false, ref sphereProjection);
            GL.Uniform4(GL.GetUniformLocation(_sphereShader, "color"), new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
            _whiteSphere.Draw();

            // Коричневая полупрозрачная сфера
            GL.Uniform4(GL.GetUniformLocation(_sphereShader, "color"), new Vector4(0.6f, 0.4f, 0.2f, 0.5f));
            _brownSphere.Draw();

            // Рендеринг частиц
            GL.UseProgram(_shaderProgram);

            Matrix4 model = Matrix4.Identity;
            Matrix4 view = _camera.GetViewMatrix();
            Matrix4 projection = _camera.GetProjectionMatrix();

            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "model"), false, ref model);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "projection"), false, ref projection);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "particleTexture"), 0);

            UpdateParticles((float)args.Time);

            GL.BindVertexArray(_vertexArrayObject);
            GL.DrawArrays(PrimitiveType.Points, 0, _particles.Count);

            SwapBuffers();
        }

        private void UpdateParticles(float deltaTime)
        {
            _time += deltaTime;

            _camera.Position = new Vector3(
                (float)Math.Sin(_time * 0.1f) * 15,
                (float)Math.Cos(_time * 0.15f) * 10,
                (float)Math.Cos(_time * 0.1f) * 15);
            _camera.Yaw = _time * 10;
            _camera.Pitch = (float)Math.Sin(_time * 0.2f) * 15;

            float[] particleData = new float[_particles.Count * 8];
            int dataIndex = 0;

            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];

                p.Position += p.Velocity;
                p.Life -= deltaTime * 50;

                if (p.Life <= 0)
                {
                    _particles[i] = CreateParticle();
                    p = _particles[i];
                }

                particleData[dataIndex++] = p.Position.X;
                particleData[dataIndex++] = p.Position.Y;
                particleData[dataIndex++] = p.Position.Z;

                particleData[dataIndex++] = p.Color.X;
                particleData[dataIndex++] = p.Color.Y;
                particleData[dataIndex++] = p.Color.Z;
                particleData[dataIndex++] = p.Color.W;

                particleData[dataIndex++] = p.Size;
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, particleData.Length * sizeof(float), particleData, BufferUsageHint.StreamDraw);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
            _camera.AspectRatio = e.Width / (float)e.Height;
        }

        

        // Модифицированный метод для обработки кнопок мыши
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Close(); // Закрываем при нажатии любой кнопки мыши
        }

        // Модифицированный метод для обработки клавиш
        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            Close(); // Закрываем при нажатии любой клавиши
        }


        // Новый метод для обработки движения мыши
        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_mouseMoved)
            {
                // Первое движение - запоминаем позицию
                _lastMousePosition = new Vector2(e.X, e.Y);
                _mouseMoved = true;
            }
            else
            {
                // Проверяем дистанцию движения
                var delta = new Vector2(e.X, e.Y) - _lastMousePosition;
                if (delta.Length > 10.0f) // Порог в 10 пикселей
                {
                    Close();
                }
            }
        }

        protected override void OnUnload()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            GL.DeleteBuffer(_vertexBufferObject);
            GL.DeleteVertexArray(_vertexArrayObject);
            GL.DeleteProgram(_shaderProgram);
            GL.DeleteTexture(_texture);
            GL.DeleteProgram(_sphereShader);

            base.OnUnload();
        }

        static void Main(string[] args)
        {
            using (var screensaver = new Screensaver())
            {
                screensaver.Run();
            }
        }
    }
}