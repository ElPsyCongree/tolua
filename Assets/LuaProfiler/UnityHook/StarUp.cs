/*
* ==============================================================================
* Filename: StarUp
* Created:  2018/7/2 11:36:16
* Author:   エル・プサイ・コングルゥ
* Purpose:  
* ==============================================================================
*/

#if UNITY_EDITOR
using LuaInterface;
using System;
using System.Reflection;
using UnityEditor;

[InitializeOnLoad]
public static class Startup
{
    private static MethodHooker hookNewLuaEnv;

    public static readonly string luaPath;
    static Startup()
    {
        HookNewLuaEnv();
    }

    public static void HookNewLuaEnv()
    {
        if (hookNewLuaEnv == null)
        {
            Type envReplace = typeof(LuaEnvReplace);
            Type typeEnv = typeof(LuaState);
            var clickFun = typeEnv.GetMethod("Start");
            MethodInfo clickReplace = envReplace.GetMethod("Start");
            MethodInfo clickProxy = envReplace.GetMethod("Proxy", BindingFlags.Public | BindingFlags.Static);
            hookNewLuaEnv = new MethodHooker(clickFun, clickReplace, clickProxy); 

            hookNewLuaEnv.Install();
        }
    }

    public static class LuaEnvReplace
    {
        public static void Start(LuaState env)
        {
            Proxy(env);
            MikuLuaProfiler.LuaProfiler.SetMainLuaEnv(env);

        }
        public static void Proxy(LuaState env)
        {
        }
    }
}
#endif