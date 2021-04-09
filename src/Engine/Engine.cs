﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SDL2.SDL;

using GEngine.Game;
using System.Diagnostics;

namespace GEngine.Engine
{
    public class EngineProperties
    {
        public double TargetTPS { get; set; }
        public double TargetFPS { get; set; }
        public double TPSOffset { get; set; }
        public double FPSOffset { get; set; }
        public bool EnableFramelimiter { get; set; }
        public string Title { get; set; }
        public double TargetFrametime
        {
            get
            {
                return (1000.00 / TargetFPS) + FPSOffset;
            }
        }
        public double TargetLogictime
        {
            get
            {
                return (1000.00 / TargetTPS) + TPSOffset;
            }
        }

        public EngineProperties()
        {
            TargetTPS = 64;
            TargetFPS = 60;
            TPSOffset = -0.15; //Play around with this to time the game speed.
            FPSOffset = -0.02; //Play around with this to time the fps. (Try: -0.02 & -0.15)
            EnableFramelimiter = true;
            Title = "GEngine | Re:";
        }
    }

    public class GameEngineEventArgs : EventArgs
    {
        public GameEngineEventType EventType { get; set; }
    }

    public class GameEngine
    {
        //Engine Props
        public EngineMode Mode { get; set; }
        public EngineProperties Properties { get; set; }
        private VideoBackend _vBackend { get; set; }

        //Threads
        private Thread _SyncThread, _AsyncThread_L, _AsyncThread_D;
        private bool _Started = false;
        private bool _StopThread = false, _ForcedThread = false;
        private bool _Aborted_S = false, _Aborted_AL = false, _Aborted_AG = false;

        //Sub-Modules
        private AudioEngine _audio;
        private GraphicsEngine _graphics;
        private InputManager _input;
        private SceneManager _scenes;
        private ResourceManager _resource;

        public AudioEngine AudioEngine
        {
            get
            {
                return _audio;
            }
        }
        public GraphicsEngine GraphicsEngine
        {
            get
            {
                return _graphics;
            }
        }
        public InputManager InputManager
        {
            get
            {
                return _input;
            }
        }
        public SceneManager SceneManager
        {
            get
            {
                return _scenes;
            }
        }
        public ResourceManager ResourceManager
        {
            get
            {
                return _resource;
            }
        }

        //SDL Stuff
        private IntPtr _SDL_Renderer;
        private IntPtr _SDL_Window;

        //init stuff
        private bool _initLogic = false, _initGraphics = false;
        private byte test = 0;
        private bool rev = false;

        //Window Stuff
        private bool _allowClose = true, _handleClose = false;
        public Size WindowSize
        {
            get
            {
                SDL_GetWindowSize(_SDL_Window, out int w, out int h);
                return new Size(w, h);
            }
        }
        public bool AllowClose
        {
            get
            {
                return _allowClose;
            }
            set
            {
                _allowClose = value;
            }
        }
        public bool HandleClose
        {
            get
            {
                return _handleClose;
            }
            set
            {
                _handleClose = value;
            }
        }

        //OpenGL fix
        private bool _rebuilt = false;
        private double _rebuildOnCall
        {
            get
            {
                return Math.Ceiling(Properties.TargetFPS / 10); //Rebuild after 1/32 fps
            }
        }
        private int _rebuildCurrentCall = 0;

        //Stats
        private double _cur_frametime = 0, _cur_logictime = 0, _cur_totaltime = 0;
        private const uint _timeMargin = 3;
        private double _fps = 0, _tps = 0;
        public double CurrentFrametime
        {
            get
            {
                return _cur_frametime;
            }
        }
        public double CurrentLogictime
        {
            get
            {
                return _cur_logictime;
            }
        }
        public double TotalTime
        {
            get
            {
                return _cur_totaltime;
            }
        }
        public bool PoorFramerate
        {
            get
            {
                return CurrentFrametime > (Properties.TargetFrametime + _timeMargin);
            }
        }
        public bool PoorLogicrate
        {
            get
            {
                return CurrentLogictime > (Properties.TargetLogictime + _timeMargin);
            }
        }
        public double FPS
        {
            get
            {
                return _fps;
            }
        }
        public double TPS
        {
            get
            {
                return _tps;
            }
        }
        public bool Running
        {
            get
            {
                return !(!_Started || _StopThread);
            }
        }

        //Special Props
        public bool LogicPause { get; set; }
        public bool DrawPause { get; set; }

        //Events
        public delegate void GameEngineEventHandler(GameEngineEventArgs eventArgs);
        public event GameEngineEventHandler OnWindowClose;

        public GameEngine(EngineMode mode = EngineMode.Synchronous, VideoBackend backend = VideoBackend.Auto)
        {
            Properties = new EngineProperties();
            _resource = new ResourceManager(_SDL_Renderer); //I don't know if this would work.
            _audio = new AudioEngine(_resource);

            if (!IsRenderDriverAvailable(backend))
            {
                Debug.Log("GameEngine.GameEngine()", $"Render Driver '{BackendToString(backend)}' is not available. Switched to software fallback.");
                backend = VideoBackend.Software;
            }

            _graphics = new GraphicsEngine(backend);
            _input = new InputManager();
            _scenes = new SceneManager();
            _vBackend = backend;

            _input.WindowEvent += InputHandler_WindowEvent;
            _input.EngineEvent += _input_EngineEvent;
        }

        public string BackendToString(VideoBackend backend)
        {
            switch (backend)
            {
                case VideoBackend.Direct3D:
                    return "direct3d";
                case VideoBackend.OpenGL:
                    return "opengl";
                case VideoBackend.OpenGL_ES:
                    return "opengles";
                case VideoBackend.OpenGL_ES2:
                    return "opengles2";
                case VideoBackend.Metal:
                    return "metal";
                case VideoBackend.Software:
                    return "software";
                case VideoBackend.Auto:
                    return "auto";
                default:
                    return "n/a";
            }
        }

        public bool IsRenderDriverAvailable(VideoBackend backend)
        {
            if (backend == VideoBackend.Auto) return true;
            string[] drivers = GetAvailableRenderDrivers();
            for (int i = 0; i < drivers.Length; ++i)
                if (drivers[i] == BackendToString(backend)) return true;
            return false;
        }

        public string[] GetAvailableRenderDrivers()
        {
            string[] ret = new string[SDL_GetNumRenderDrivers()];
            for (int i = 0; i < ret.Length; ++i)
            {
                SDL_GetRenderDriverInfo(i, out SDL_RendererInfo info);
                ret[i] = UTF8_ToManaged(info.name);
            }
            return ret;
        }

        private void _input_EngineEvent(InputCallbackEventArg eventArg)
        {
            switch (eventArg.CallbackType)
            {
                case InputCallbackType.RenderDeviceReset:
                    _resource.RebuildTextures();
                    break;
            }
        }

        private void InputHandler_WindowEvent(InputCallbackEventArg eventArg)
        {
            switch (eventArg.CallbackType)
            {
                case InputCallbackType.WindowClose:
                    if (_allowClose)
                        if (_handleClose)
                        {
                            ForceStop();
                        }
                        else
                        {
                            OnWindowClose?.Invoke(new GameEngineEventArgs()
                            {
                                EventType = GameEngineEventType.WindowClose
                            });
                        }
                    break;
                case InputCallbackType.FocusGained:
                    if (_vBackend == VideoBackend.OpenGL || _vBackend == VideoBackend.OpenGL_ES || _vBackend == VideoBackend.OpenGL_ES2)
                    {
                        _resource.RebuildTextures();
                    }
                    break;
            }
        }

        public void Start()
        {
            switch (Mode)
            {
                case EngineMode.Synchronous:
                    _Started = true;
                    _SyncThread = new Thread(new ThreadStart(Sync_Loop));
                    _SyncThread.Name = "Synchronous Game Thread";
                    _SyncThread.Start();
                    break;
                case EngineMode.Asynchronous:
                    _Started = true;

                    break;
                default:
                    //unknown mode
                    throw new EngineException("Unknown engine mode.", "GameEngine.Start()");
            }
            while (_SDL_Renderer == IntPtr.Zero || _SDL_Window == IntPtr.Zero) Thread.Sleep(10);
            _resource.SetRenderer(_SDL_Renderer);
        }
        public void Stop()
        {
            _StopThread = true;
            Thread.Sleep(10);
            if (!_ForcedThread)
                switch (Mode)
                {
                    case EngineMode.Synchronous:
                        while (!_Aborted_S) Thread.Sleep(10);
                        break;
                    case EngineMode.Asynchronous:
                        while (!_Aborted_AL || !_Aborted_AG) Thread.Sleep(10);
                        break;
                    default:
                        //unknown mode
                        throw new EngineException("Unknown engine mode.", "GameEngine.Stop()");
                }
            FreeResources();
            SDL_Quit();
            Debug.Log("GameEngine.Stop()", "Engine stopped.");
        }
        private void FreeResources()
        {
            SDL_DestroyRenderer(_SDL_Renderer);
            SDL_DestroyWindow(_SDL_Window);
            _resource.Quit();
        }
        private void ForceStop()
        {
            _StopThread = true;
            _ForcedThread = true;
            Thread.Sleep(10);
            FreeResources();
            SDL_Quit();
            Debug.Log("GameEngine.ForceStop()", "Engine forcibly stopped.");
        }
        private void InitLogic()
        {
            if (!_initLogic)
            {
                _audio.Init();
                _input.Init();
                _initLogic = true;
            }
        }
        private void InitGraphics()
        {
            if (!_initGraphics)
            {
                _graphics.Init();
                _graphics.CreateWindow(Properties.Title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, 800, 600);
                _graphics.CreateRenderer();
                _SDL_Renderer = _graphics.Renderer;
                _SDL_Window = _graphics.Window;
                _graphics.RenderClearColor = new ColorRGBA(120, 180, 230);
                _initGraphics = true;
            }
        }
        private void LogicStep()
        {
            _input.PollEvent();
            if (!LogicPause)
            {
                //Do stuff here
                if (test == 0)
                {
                    rev = false;
                } else if (test == 255)
                {
                    rev = true;
                }
                if (rev)
                {
                    test--;
                } else
                {
                    test++;
                }
                //Console.WriteLine(test);

                string s = SDL_GetError();
                if (s != "" && !_StopThread)
                {
                    ForceStop();
                    throw new EngineException("Unexpected SDL Error occured, engine halted.", "GameEngine.LogicStep()");
                }
            }
        }
        private void DrawStep()
        {
            if (!DrawPause)
            {
                _graphics.RenderClear();
                if (!_rebuilt)
                {
                    if (_rebuildCurrentCall > _rebuildOnCall)
                    {
                        if (_vBackend == VideoBackend.OpenGL || _vBackend == VideoBackend.OpenGL_ES || _vBackend == VideoBackend.OpenGL_ES2)
                        {
                            _resource.RebuildTextures();
                        }
                        _rebuilt = true;
                    } else
                    {
                        _rebuildCurrentCall++;
                    }
                }
                SDL_Rect rectangle = new SDL_Rect();
                rectangle.x = 420;
                rectangle.y = 420;
                rectangle.w = 300;
                rectangle.h = 200;
                SDL_Rect s = new SDL_Rect();
                s.x = 0;
                s.y = 0;
                s.w = 400;
                s.h = 400;
                _graphics.SetRenderDrawColor(new ColorRGBA(test, test, test));
                //SDL_RenderDrawRectF(_SDL_Renderer, ref rectangle);
                SDL_RenderFillRect(_SDL_Renderer, ref rectangle);
                _graphics.SetRenderDrawColor(new ColorRGBA(255, 255, 255));
                if (_resource.HasTexture("spr_test"))
                {
                    if (!_StopThread && _resource.GetTextureResource("spr_test").Textures.Length > 0)
                    {
                        int e = SDL_RenderCopy(_SDL_Renderer, _resource.GetTextureResource("spr_test").Textures[0], ref s, ref s);
                        if (e != 0)
                        {
                            Debug.Log("GameEngine.DrawStep()", "Error rendering rexture: " + SDL_GetError());
                            throw new EngineException();
                        }
                    }
                }
                SDL_RenderPresent(_SDL_Renderer);
            }
        }
        private void Sync_Loop()
        {
            //Init
            InitLogic();
            InitGraphics();
            Stopwatch logicTimer = new Stopwatch();
            Stopwatch drawTimer = new Stopwatch();
            Sampler fpsAvg = new Sampler(100);
            Sampler tpsAvg = new Sampler(100);
            logicTimer.Start();
            drawTimer.Start();
            double total;
            bool flip = false;
            do
            {
                total = GetPreciseMs();
                if (ETtoMS(logicTimer.ElapsedTicks) >= Properties.TargetLogictime)
                {
                    _cur_logictime = ETtoMS(logicTimer.ElapsedTicks);
                    tpsAvg.AddPoint(1000.00/_cur_logictime);
                    logicTimer.Restart();
                    LogicStep();
                }
                if (ETtoMS(drawTimer.ElapsedTicks) >= Properties.TargetFrametime || !Properties.EnableFramelimiter)
                {
                    if (!flip)
                    {
                        _cur_frametime = ETtoMS(drawTimer.ElapsedTicks);
                        fpsAvg.AddPoint(1000.00 / _cur_frametime);
                        drawTimer.Restart();
                        DrawStep();
                    }
                    if (!Properties.EnableFramelimiter) flip = !flip;
                }
                total = GetPreciseMs() - total;
                _cur_totaltime = total;
                _fps = fpsAvg.GetAverage();
                _tps = tpsAvg.GetAverage();
            } while (!_StopThread);
            _Aborted_S = true;
        }
        private void Async_LogicLoop()
        {
            //Init
            InitLogic();
            do
            {
                LogicStep();
            } while (!_StopThread);
        }
        private void Async_DrawLoop()
        {
            //Init
            InitGraphics();
            do
            {
                DrawStep();
            } while (!_StopThread);
        }
        private static double TicksToMs(long nano)
        {
            return (double)((decimal)nano / (decimal)10000.0000);
        }
        private static double GetPreciseMs()
        {
            return TicksToMs(DateTime.Now.Ticks);
        }
        private static void PreciseWait(double ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            var match = Math.Round((ms/1000) * Stopwatch.Frequency);
            while (sw.ElapsedTicks < match) { }
        }
        private static double ETtoMS(long timerTick)
        {
            return ((double)timerTick / (double)Stopwatch.Frequency) * 1000.00;
        }
    }
}
