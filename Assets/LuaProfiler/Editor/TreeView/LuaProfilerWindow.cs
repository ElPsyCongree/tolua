﻿/*
* ==============================================================================
* Filename: LuaExport
* Created:  2018/7/13 14:29:22
* Author:   エル・プサイ・コングルゥ
* Purpose:  
* ==============================================================================
*/
using UnityEngine;
using UnityEditor.IMGUI.Controls;
using UnityEditor;
using System;

namespace MikuLuaProfiler
{
#if UNITY_5_6_OR_NEWER
    public class LuaProfilerWindow : EditorWindow
    {
        [SerializeField] TreeViewState m_TreeViewState;

        LuaProfilerTreeView m_TreeView;
        SearchField m_SearchField;

        void OnEnable()
        {
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();

            LuaProfilerTreeView.m_nodeDict.Clear();
            m_TreeView = new LuaProfilerTreeView(m_TreeViewState, position.width - 40);
            m_SearchField = new SearchField();
            m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;
        }
        void OnGUI()
        {
            DoToolbar();
            DoTreeView();
        }

        private bool m_isStop = false;
        void DoToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            #region clear
            bool isClear = GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Height(30));
            if (isClear)
            {
                m_TreeView.Clear();
            }
            GUILayout.Space(5);
            #endregion

            #region deep
            bool flag = GUILayout.Toggle(LuaDeepProfilerSetting.Instance.isDeepProfiler,
                "Deep Profiler", EditorStyles.toolbarButton, GUILayout.Height(30));
            if (flag != LuaDeepProfilerSetting.Instance.isDeepProfiler)
            {
                LuaDeepProfilerSetting.Instance.isDeepProfiler = flag;
            }
            GUILayout.Space(5);
            #endregion

            #region stop
            bool isStop = GUILayout.Toggle(m_isStop, "Stop GC", EditorStyles.toolbarButton, GUILayout.Height(30));

            if (m_isStop != isStop)
            {
                if (isStop)
                {
                    var env = LuaProfiler.mainL;
                    if (env != IntPtr.Zero)
                    {
                        LuaInterface.LuaDLL.lua_gc(env, LuaInterface.LuaGCOptions.LUA_GCSTOP, 0);
                    }
                    m_isStop = true;
                }
                else
                {
                    var env = LuaProfiler.mainL;
                    if (env != IntPtr.Zero)
                    {
                        LuaInterface.LuaDLL.lua_gc(env, LuaInterface.LuaGCOptions.LUA_GCRESTART, 0);
                    }
                    m_isStop = false;
                }
            }
            GUILayout.Space(5);
            #endregion

            #region run gc
            bool isRunGC = GUILayout.Button("Run GC", EditorStyles.toolbarButton, GUILayout.Height(30));
            if (isRunGC)
            {
                var env = LuaProfiler.mainL;
                if (env != IntPtr.Zero)
                {
                    LuaInterface.LuaDLL.lua_gc(env, LuaInterface.LuaGCOptions.LUA_GCCOLLECT, 0);
                }
            }
            GUILayout.Space(20);
            GUILayout.FlexibleSpace();
            #endregion

            #region gc value
            GUILayout.Label(string.Format("Lua Total:{0}", LuaProfiler.GetLuaMemory()), EditorStyles.toolbarButton, GUILayout.Height(30));
            #endregion

            GUILayout.Space(100);
            GUILayout.FlexibleSpace();
            m_TreeView.searchString = m_SearchField.OnToolbarGUI(m_TreeView.searchString);
            GUILayout.EndHorizontal();
        }

        void DoTreeView()
        {
            Rect rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            m_TreeView.Reload();
            m_TreeView.OnGUI(rect);
        }

        // Add menu named "My Window" to the Window menu
        [MenuItem("Window/Lua Profiler Window")]
        static public void ShowWindow()
        {
            // Get existing open window or if none, make a new one:
            var window = GetWindow<LuaProfilerWindow>();
            window.titleContent = new GUIContent("Lua Profiler");
            window.Show();
        }
    }
#endif
}
