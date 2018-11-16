#pragma warning disable CS0219//故意在c#这里产生于lua那边的等量GC
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_5_5_OR_NEWER
using UnityEngine.Profiling;
#endif
using LuaInterface;
using LuaLib = LuaInterface.LuaDLL;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEditorInternal;
using UnityEditor;

namespace MikuLuaProfiler
{

    [InitializeOnLoad]
    static class HookSetup
    {
#if !UNITY_2017_1_OR_NEWER
        static bool isPlaying = false;
#endif
        static HookSetup()
        {
#if UNITY_2017_1_OR_NEWER
            EditorApplication.playModeStateChanged += OnEditorPlaying;
#else
            EditorApplication.playmodeStateChanged += () =>
            {

                if (isPlaying == true && EditorApplication.isPlaying == false)
                {
                    LuaProfiler.SetMainLuaEnv(null);
                }

                isPlaying = EditorApplication.isPlaying;
            };
#endif
        }

#if UNITY_2017_1_OR_NEWER
        public static void OnEditorPlaying(PlayModeStateChange playModeStateChange)
        {
            if (playModeStateChange == PlayModeStateChange.ExitingPlayMode)
            {
                LuaProfiler.SetMainLuaEnv(null);
            }
        }
#endif

        #region hook

        #region hook tostring

        public class LuaDll
        {
            #region luastring
            public static readonly Dictionary<long, string> stringDict = new Dictionary<long, string>();
            public static bool TryGetLuaString(IntPtr p, out string result)
            {

                return stringDict.TryGetValue((long)p, out result);
            }
            public static void RefString(IntPtr strPoint, int index, string s, IntPtr L)
            {
                int oldTop = LuaLib.lua_gettop(L);
                LuaLib.lua_pushvalue(L, index);
                //把字符串ref了之后就不GC了
                LuaLib.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
                LuaLib.lua_settop(L, oldTop);
                stringDict[(long)strPoint] = s;
            }
            #endregion

            public static int luaL_loadbuffer(IntPtr luaState, byte[] buff, int size, string name)
            {
                if (LuaDeepProfilerSetting.Instance.isDeepProfiler)//&& name != "chunk"
                {
                    var utf8WithoutBom = new System.Text.UTF8Encoding(true);
                    string fileName = name.Replace("@", "").Replace("/", ".") + ".lua";
                    string value = utf8WithoutBom.GetString(buff);
                    value = Parse.InsertSample(value, fileName);

                    //System.IO.File.WriteAllText(fileName, value);

                    buff = utf8WithoutBom.GetBytes(value);
                    size = buff.Length;
                }

                return ProxyLoadbuffer(luaState, buff, size, name);
            }

            public static int ProxyLoadbuffer(IntPtr L, byte[] buff, int size, string name)
            {
                return 0;
            }

            public static string lua_tostring(IntPtr luaState, int index)
            {
                int len = 0;
                IntPtr str = LuaLib.tolua_tolstring(luaState, index, out len);

                if (str != IntPtr.Zero)
                {
                    string s;
                    if (!TryGetLuaString(str, out s))
                    {
                        s = LuaLib.lua_ptrtostring(str, len);
                    }
                    return s;
                }

                return null;
            }

            public static string PoxyToString(IntPtr L, int index)
            {
                return null;
            }
        }
        #endregion


        #region hook profiler
        public class Profiler
        {
            private static Stack<string> m_Stack = new Stack<string>();
            private static int m_currentFrame = 0;
            public static void BeginSampleOnly(string name)
            {
                if (ProfilerDriver.deepProfiling) return;
                if (Time.frameCount != m_currentFrame)
                {
                    m_Stack.Clear();
                    m_currentFrame = Time.frameCount;
                }
                m_Stack.Push(name);
                ProxyBeginSample(name);
            }
            public static void BeginSample(string name, UnityEngine.Object targetObject)
            {
                if (ProfilerDriver.deepProfiling) return;
                m_Stack.Push(name);
                ProxyBeginSample(name, targetObject);
            }

            public static void EndSample()
            {
                if (ProfilerDriver.deepProfiling) return;
                if (m_Stack.Count <= 0)
                {
                    return;
                }
                m_Stack.Pop();
                ProxyEndSample();
            }

            public static void ProxyBeginSample(string name)
            {
            }
            public static void ProxyBeginSample(string name, UnityEngine.Object targetObject)
            {
            }

            public static void ProxyEndSample()
            {
            }
        }
        #endregion

        #region do hook
        private static MethodHooker beginSampeOnly;
        private static MethodHooker beginObjetSample;
        private static MethodHooker endSample;
        private static MethodHooker tostringHook;
        private static MethodHooker loaderHook;

        private static bool m_hooked = false;
        public static void HookLuaFuns()
        {
            if (m_hooked) return;
            if (tostringHook == null)
            {
                Type typeLogReplace = typeof(LuaDll);
                Type typeLog = typeof(LuaLib);
                MethodInfo tostringFun = typeLog.GetMethod("lua_tostring");
                MethodInfo tostringReplace = typeLogReplace.GetMethod("lua_tostring");
                MethodInfo tostringProxy = typeLogReplace.GetMethod("ProxyToString");

                tostringHook = new MethodHooker(tostringFun, tostringReplace, tostringProxy);
                tostringHook.Install();

                tostringFun = typeLog.GetMethod("luaL_loadbuffer");
                tostringReplace = typeLogReplace.GetMethod("luaL_loadbuffer");
                tostringProxy = typeLogReplace.GetMethod("ProxyLoadbuffer");

                tostringHook = new MethodHooker(tostringFun, tostringReplace, tostringProxy);
                tostringHook.Install();
            }

            if (beginSampeOnly == null)
            {
                Type typeTarget = typeof(UnityEngine.Profiling.Profiler);
                Type typeReplace = typeof(Profiler);

                MethodInfo hookTarget = typeTarget.GetMethod("BeginSampleOnly", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
                MethodInfo hookReplace = typeReplace.GetMethod("BeginSampleOnly");
                MethodInfo hookProxy = typeReplace.GetMethod("ProxyBeginSample", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
                beginSampeOnly = new MethodHooker(hookTarget, hookReplace, hookProxy);
                beginSampeOnly.Install();

                hookTarget = typeTarget.GetMethod("BeginSample", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(UnityEngine.Object) }, null);
                hookReplace = typeReplace.GetMethod("BeginSample");
                hookProxy = typeReplace.GetMethod("ProxyBeginSample", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(UnityEngine.Object) }, null);
                beginObjetSample = new MethodHooker(hookTarget, hookReplace, hookProxy);
                beginObjetSample.Install();

                hookTarget = typeTarget.GetMethod("EndSample", BindingFlags.Public | BindingFlags.Static, null, new Type[] { }, null);
                hookReplace = typeReplace.GetMethod("EndSample");
                hookProxy = typeReplace.GetMethod("ProxyEndSample", BindingFlags.Public | BindingFlags.Static, null, new Type[] { }, null);
                endSample = new MethodHooker(hookTarget, hookReplace, hookProxy);
                endSample.Install();
            }

            m_hooked = true;
        }

        public static void Uninstall()
        {
            if (beginSampeOnly != null)
            {
                beginSampeOnly.Uninstall();
                beginSampeOnly = null;
            }
            if (beginObjetSample != null)
            {
                beginObjetSample.Uninstall();
                beginObjetSample = null;
            }
            if (endSample != null)
            {
                endSample.Uninstall();
                endSample = null;
            }
            if (tostringHook != null)
            {
                tostringHook.Uninstall();
                tostringHook = null;
            }
            if (loaderHook != null)
            {
                loaderHook.Uninstall();
                loaderHook = null;
            }

            m_hooked = false;
        }
        #endregion

        #endregion
    }

    public class LuaProfiler
    {
        public static LuaState mainEnv
        {
            get
            {
                return _mainEnv;
            }
        }
        private static IntPtr? _mainL = null;
        public static IntPtr mainL
        {
            get
            {
                if (_mainEnv == null)
                {
                    return IntPtr.Zero;
                }
                else
                {
                    if (_mainL == null)
                    {
                        Type t = typeof(LuaState);
                        var field = t.GetField("L", BindingFlags.NonPublic| BindingFlags.Instance);
                        var lObj = field.GetValue(mainEnv);
                        _mainL = (IntPtr)lObj;
                    }

                    return _mainL.Value;
                }
            }
        }
        private static LuaState _mainEnv;
        public static void SetMainLuaEnv(LuaState env)
        {
            //不支持多栈
            if (_mainEnv != null && env != null) return;

            _mainEnv = env;
            if (LuaDeepProfilerSetting.Instance.isDeepProfiler)
            {
                if (env != null)
                {
                    env.BeginModule(null);
                    env.BeginModule("MikuLuaProfiler");
                    MikuLuaProfiler_LuaProfilerWrap.Register(env);
                    env.EndModule();
                    env.EndModule();

                    env.DoString(@"
BeginMikuSample = MikuLuaProfiler.LuaProfiler.BeginSample
EndMikuSample = MikuLuaProfiler.LuaProfiler.EndSample

function miku_unpack_return_value(...)
	EndMikuSample()
	return ...
end
");
                    HookSetup.HookLuaFuns();
                }
            }

            if (env == null)
            {
                HookSetup.Uninstall();
                _mainL = null;
            }
        }

        public static string GetLuaMemory()
        {
            long result = 0;
            if (mainL != IntPtr.Zero)
            {
                try
                {
                    result = GetLuaMemory(mainL);
                }
                catch { }
            }

            return GetMemoryString(result);
        }

        public static long GetLuaMemory(IntPtr luaState)
        {
            long result = 0;

            result = LuaLib.lua_gc(luaState, LuaGCOptions.LUA_GCCOUNT, 0);
            result = result * 1024 + LuaLib.lua_gc(luaState, LuaGCOptions.LUA_GCCOUNTB, 0);

            return result;
        }

        public class Sample
        {
            private static ObjectPool<Sample> samplePool = new ObjectPool<Sample>(250);
            public static Sample Create(float time, long memory, string name)
            {
                Sample s = samplePool.GetObject();
                s.currentTime = time;
                s.currentLuaMemory = memory;
                s.realCurrentLuaMemory = memory;
                s.costGC = 0;
                s.name = name;
                s.costTime = 0;
                s.childs.Clear();
                s._father = null;
                s._fullName = null;

                return s;
            }

            public void Restore()
            {
                for (int i = 0, imax = childs.Count; i < imax; i++)
                {
                    childs[i].Restore();
                }
                samplePool.Store(this);
            }

            public int oneFrameCall
            {
                get
                {
                    return 1;
                }
            }
            public float currentTime { private set; get; }
            public long realCurrentLuaMemory { private set; get; }
            private string _name;
            public string name
            {
                private set
                {
                    _name = value;
                }
                get
                {
                    return _name;
                }
            }

            private static Dictionary<string, Dictionary<string, string>> m_fullNamePool = new Dictionary<string, Dictionary<string, string>>();
            private string _fullName = null;
            public string fullName
            {
                get
                {
                    if (_father == null) return _name;

                    if (_fullName == null)
                    {
                        Dictionary<string, string> childDict;
                        if (!m_fullNamePool.TryGetValue(_father.fullName, out childDict))
                        {
                            childDict = new Dictionary<string, string>();
                            m_fullNamePool.Add(_father.fullName, childDict);
                        }

                        if (!childDict.TryGetValue(_name, out _fullName))
                        {
                            string value = _name;
                            var f = _father;
                            while (f != null)
                            {
                                value = f.name + value;
                                f = f.fahter;
                            }
                            _fullName = value;
                            childDict[_name] = _fullName;
                        }

                        return _fullName;
                    }
                    else
                    {
                        return _fullName;
                    }
                }
            }
            //这玩意在统计的window里面没啥卵用
            public long currentLuaMemory { set; get; }

            private float _costTime;
            public float costTime
            {
                set
                {
                    _costTime = value;
                }
                get
                {
                    float result = _costTime;
                    return result;
                }
            }

            private long _costGC;
            public long costGC
            {
                set
                {
                    _costGC = value;
                }
                get
                {
                    return _costGC;
                }
            }
            private Sample _father;
            public Sample fahter
            {
                set
                {
                    _father = value;
                    if (_father != null)
                    {
                        _father.childs.Add(this);
                    }
                }
                get
                {
                    return _father;
                }
            }

            public readonly List<Sample> childs = new List<Sample>(256);
        }
        //开始采样时候的lua内存情况，因为中间有可能会有二次采样，所以要丢到一个盏中
        public static readonly List<Sample> beginSampleMemoryStack = new List<Sample>();

        private static Action<Sample> m_SampleEndAction;

        private static bool isDeep
        {
            get
            {
#if UNITY_EDITOR
                return ProfilerDriver.deepProfiling;
#else
            return false;
#endif
            }
        }
        public static void SetSampleEnd(Action<Sample> action)
        {
            m_SampleEndAction = action;
        }
        public static void BeginSample(string name)
        {
#if DEBUG
            if (_mainEnv != null)
            {
                BeginSample(mainL, name);
            }
#endif
        }
        public static void BeginSample(IntPtr luaState)
        {
#if DEBUG
            BeginSample(luaState, "lua gc");
#endif
        }
        private static int m_currentFrame = 0;
        public static void BeginSample(IntPtr luaState, string name)
        {
            if (m_currentFrame != Time.frameCount)
            {
                PopAllSampleWhenLateUpdate();
                m_currentFrame = Time.frameCount;
            }

#if DEBUG
            //if (beginSampleMemoryStack.Count == 0 && LuaDeepProfilerSetting.Instance.isDeepProfiler)
            //    LuaLib.lua_gc(luaState, LuaGCOptions.LUA_GCSTOP, 0);

            long memoryCount = GetLuaMemory(luaState);
            Sample sample = Sample.Create(Time.realtimeSinceStartup, memoryCount, name);

            beginSampleMemoryStack.Add(sample);
            if (!isDeep)
            {
                Profiler.BeginSample(name);
            }
#endif
        }
        public static void PopAllSampleWhenLateUpdate()
        {
            for (int i = 0, imax = beginSampleMemoryStack.Count; i < imax; i++)
            {
                var item = beginSampleMemoryStack[i];
                if (item.fahter == null)
                {
                    item.Restore();
                }
            }
            beginSampleMemoryStack.Clear();
        }
        public static void EndSample()
        {
#if DEBUG
            if (_mainEnv != null)
            {
                EndSample(mainL);
            }
#endif
        }
        public static void EndSample(IntPtr luaState)
        {
#if DEBUG
            if (beginSampleMemoryStack.Count <= 0)
            {
                return;
            }
            int count = beginSampleMemoryStack.Count;
            Sample sample = beginSampleMemoryStack[beginSampleMemoryStack.Count - 1];
            long oldMemoryCount = sample.currentLuaMemory;
            beginSampleMemoryStack.RemoveAt(count - 1);
            long nowMemoryCount = GetLuaMemory(luaState);
            sample.fahter = count > 1 ? beginSampleMemoryStack[count - 2] : null;

            if (!isDeep)
            {
                long delta = nowMemoryCount - oldMemoryCount;

                long tmpDelta = delta;
                if (delta > 0)
                {
                    delta = Math.Max(delta - 40, 0);//byte[0] 的字节占用是40
                    byte[] luagc = new byte[delta];
                }
                for (int i = 0, imax = beginSampleMemoryStack.Count; i < imax; i++)
                {
                    Sample s = beginSampleMemoryStack[i];
                    s.currentLuaMemory += tmpDelta;
                    beginSampleMemoryStack[i] = s;
                }
                Profiler.EndSample();
            }

            sample.costTime = Time.realtimeSinceStartup - sample.currentTime;
            var gc = nowMemoryCount - sample.realCurrentLuaMemory;
            sample.costGC = gc > 0 ? gc : 0;
            //if (beginSampleMemoryStack.Count == 0 && LuaDeepProfilerSetting.Instance.isDeepProfiler)
            //{
            //    LuaLib.lua_gc(luaState, LuaGCOptions.LUA_GCRESTART, 0);
            //    LuaLib.lua_gc(luaState, LuaGCOptions.LUA_GCCOLLECT, 0);
            //}


            if (m_SampleEndAction != null && beginSampleMemoryStack.Count == 0)
            {
                m_SampleEndAction(sample);
            }

            if (sample.fahter == null)
            {
                sample.Restore();
            }
#endif
        }

        const long MaxB = 1024;
        const long MaxK = MaxB * 1024;
        const long MaxM = MaxK * 1024;
        const long MaxG = MaxM * 1024;

        public static string GetMemoryString(long value, string unit = "B")
        {
            string result = null;
            if (value < MaxB)
            {
                result = string.Format("{0}{1}", value, unit);
            }
            else if (value < MaxK)
            {
                result = string.Format("{0:N2}K{1}", (float)value / MaxB, unit);
            }
            else if (value < MaxM)
            {
                result = string.Format("{0:N2}M{1}", (float)value / MaxK, unit);
            }
            else if (value < MaxG)
            {
                result = string.Format("{0:N2}G{1}", (float)value / MaxM, unit);
            }
            return result;
        }
    }
}


#region bind
public class MikuLuaProfiler_LuaProfilerWrap
{
    public static void Register(LuaState L)
    {
        L.BeginClass(typeof(MikuLuaProfiler.LuaProfiler), typeof(System.Object));
        L.RegFunction("SetMainLuaEnv", SetMainLuaEnv);
        L.RegFunction("GetLuaMemory", GetLuaMemory);
        L.RegFunction("SetSampleEnd", SetSampleEnd);
        L.RegFunction("BeginSample", BeginSample);
        L.RegFunction("PopAllSampleWhenLateUpdate", PopAllSampleWhenLateUpdate);
        L.RegFunction("EndSample", EndSample);
        L.RegFunction("GetMemoryString", GetMemoryString);
        L.RegFunction("New", _CreateMikuLuaProfiler_LuaProfiler);
        L.RegFunction("__tostring", ToLua.op_ToString);
        L.RegVar("beginSampleMemoryStack", get_beginSampleMemoryStack, null);
        L.RegVar("mainEnv", get_mainEnv, null);
        L.RegVar("mainL", get_mainL, null);
        L.EndClass();
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int _CreateMikuLuaProfiler_LuaProfiler(IntPtr L)
    {
        try
        {
            int count = LuaDLL.lua_gettop(L);

            if (count == 0)
            {
                MikuLuaProfiler.LuaProfiler obj = new MikuLuaProfiler.LuaProfiler();
                ToLua.PushObject(L, obj);
                return 1;
            }
            else
            {
                return LuaDLL.luaL_throw(L, "invalid arguments to ctor method: MikuLuaProfiler.LuaProfiler.New");
            }
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int SetMainLuaEnv(IntPtr L)
    {
        try
        {
            ToLua.CheckArgsCount(L, 1);
            LuaInterface.LuaState arg0 = (LuaInterface.LuaState)ToLua.CheckObject<LuaInterface.LuaState>(L, 1);
            MikuLuaProfiler.LuaProfiler.SetMainLuaEnv(arg0);
            return 0;
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int GetLuaMemory(IntPtr L)
    {
        try
        {
            int count = LuaDLL.lua_gettop(L);

            if (count == 0)
            {
                string o = MikuLuaProfiler.LuaProfiler.GetLuaMemory();
                LuaDLL.lua_pushstring(L, o);
                return 1;
            }
            else if (count == 1)
            {
                System.IntPtr arg0 = ToLua.CheckIntPtr(L, 1);
                long o = MikuLuaProfiler.LuaProfiler.GetLuaMemory(arg0);
                LuaDLL.tolua_pushint64(L, o);
                return 1;
            }
            else
            {
                return LuaDLL.luaL_throw(L, "invalid arguments to method: MikuLuaProfiler.LuaProfiler.GetLuaMemory");
            }
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int SetSampleEnd(IntPtr L)
    {
        try
        {
            ToLua.CheckArgsCount(L, 1);
            System.Action<MikuLuaProfiler.LuaProfiler.Sample> arg0 = (System.Action<MikuLuaProfiler.LuaProfiler.Sample>)ToLua.CheckDelegate<System.Action<MikuLuaProfiler.LuaProfiler.Sample>>(L, 1);
            MikuLuaProfiler.LuaProfiler.SetSampleEnd(arg0);
            return 0;
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int BeginSample(IntPtr L)
    {
        try
        {
            int count = LuaDLL.lua_gettop(L);

            if (count == 1 && TypeChecker.CheckTypes<System.IntPtr>(L, 1))
            {
                System.IntPtr arg0 = ToLua.CheckIntPtr(L, 1);
                MikuLuaProfiler.LuaProfiler.BeginSample(arg0);
                return 0;
            }
            else if (count == 1 && TypeChecker.CheckTypes<string>(L, 1))
            {
                string arg0 = ToLua.ToString(L, 1);
                MikuLuaProfiler.LuaProfiler.BeginSample(arg0);
                return 0;
            }
            else if (count == 2)
            {
                System.IntPtr arg0 = ToLua.CheckIntPtr(L, 1);
                string arg1 = ToLua.CheckString(L, 2);
                MikuLuaProfiler.LuaProfiler.BeginSample(arg0, arg1);
                return 0;
            }
            else
            {
                return LuaDLL.luaL_throw(L, "invalid arguments to method: MikuLuaProfiler.LuaProfiler.BeginSample");
            }
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int PopAllSampleWhenLateUpdate(IntPtr L)
    {
        try
        {
            ToLua.CheckArgsCount(L, 0);
            MikuLuaProfiler.LuaProfiler.PopAllSampleWhenLateUpdate();
            return 0;
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int EndSample(IntPtr L)
    {
        try
        {
            int count = LuaDLL.lua_gettop(L);

            if (count == 0)
            {
                MikuLuaProfiler.LuaProfiler.EndSample();
                return 0;
            }
            else if (count == 1)
            {
                System.IntPtr arg0 = ToLua.CheckIntPtr(L, 1);
                MikuLuaProfiler.LuaProfiler.EndSample(arg0);
                return 0;
            }
            else
            {
                return LuaDLL.luaL_throw(L, "invalid arguments to method: MikuLuaProfiler.LuaProfiler.EndSample");
            }
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int GetMemoryString(IntPtr L)
    {
        try
        {
            int count = LuaDLL.lua_gettop(L);

            if (count == 1)
            {
                long arg0 = LuaDLL.tolua_checkint64(L, 1);
                string o = MikuLuaProfiler.LuaProfiler.GetMemoryString(arg0);
                LuaDLL.lua_pushstring(L, o);
                return 1;
            }
            else if (count == 2)
            {
                long arg0 = LuaDLL.tolua_checkint64(L, 1);
                string arg1 = ToLua.CheckString(L, 2);
                string o = MikuLuaProfiler.LuaProfiler.GetMemoryString(arg0, arg1);
                LuaDLL.lua_pushstring(L, o);
                return 1;
            }
            else
            {
                return LuaDLL.luaL_throw(L, "invalid arguments to method: MikuLuaProfiler.LuaProfiler.GetMemoryString");
            }
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int get_beginSampleMemoryStack(IntPtr L)
    {
        try
        {
            ToLua.PushSealed(L, MikuLuaProfiler.LuaProfiler.beginSampleMemoryStack);
            return 1;
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int get_mainEnv(IntPtr L)
    {
        try
        {
            ToLua.PushObject(L, MikuLuaProfiler.LuaProfiler.mainEnv);
            return 1;
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int get_mainL(IntPtr L)
    {
        try
        {
            LuaDLL.lua_pushlightuserdata(L, MikuLuaProfiler.LuaProfiler.mainL);
            return 1;
        }
        catch (Exception e)
        {
            return LuaDLL.toluaL_exception(L, e);
        }
    }
}
#endregion

#endif

