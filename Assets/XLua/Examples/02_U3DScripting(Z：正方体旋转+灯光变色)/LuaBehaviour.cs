//作用：用 C# 写的 "桥梁" 类，作用是让 Unity 游戏引擎能运行 Lua 脚本

//思路：其实就是先设置一个LuaTable对象scriptScopeTable为每个Lua脚本创建独立的作用域；在C#脚本中用委托接收Lua中的Awake等方法，模拟生命周期的效果 

/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using XLua;
using System;

namespace XLuaTest
{

    //Z：定义 "注入数据" 的结构，用于在 Unity 编辑器中手动关联 Lua 脚本需要用到的 Unity 物体
    [System.Serializable]
    public class Injection
    {
        public string name;
        public GameObject value;
    }

    //Z：是连接 Unity 和 Lua 的 "桥梁类"，允许 Lua 脚本控制 Unity 物体的生命周期和交互
    [LuaCallCSharp]
    public class LuaBehaviour : MonoBehaviour
    {
        public TextAsset luaScript;//Z：要运行的 Lua 脚本文件（以 TextAsset 形式导入 Unity）
        public Injection[] injections; //Z：一个数组，存储需要传递给 Lua 的物体

        internal static LuaEnv luaEnv = new LuaEnv(); //all lua behaviour shared one luaenv only!
        internal static float lastGCTime = 0;//Z：记录上一次垃圾回收的时间
        internal const float GCInterval = 1;//Z：固定垃圾回收间隔（1 秒）


        //Z：C# 中的委托（可理解为 "函数容器"），用于存储从 Lua 脚本中读取的函数，后续在 Unity 生命周期中调用
        private Action luaStart;
        private Action luaUpdate;
        private Action luaOnDestroy;
        private Action<Collision2D> onCollisionEnter2D;
        private Action<Collider2D> onTriggerEnter2D;

        //Z：LuaTable是 xLua 中的 "表"（类似字典），用于为每个 Lua 脚本创建独立的作用域，避免不同 Lua 脚本的全局变量 / 函数冲突（比如 A 脚本的score和 B 脚本的score互不干扰）
        private LuaTable scriptScopeTable;

        void Awake()
        {
            // 为每个脚本设置一个独立的脚本域，可一定程度上防止脚本间全局变量、函数冲突？？？？？？？？？？？？？？
            scriptScopeTable = luaEnv.NewTable();

            // 设置其元表的 __index, 使其能够访问全局变量（luaEnv.Global）
            using (LuaTable meta = luaEnv.NewTable())
            {
                meta.Set("__index", luaEnv.Global);
                scriptScopeTable.SetMetaTable(meta);
            }

            // 将所需值注入到 Lua 脚本域中 [Z： 是往 scriptScopeTable（Lua 的 “小房间”）里放了一个叫 self 的变量，变量的值是当前的 LuaBehaviour 组件]  
            //Z：作用：Lua 脚本里可以直接用 self 访问这个组件，比如 self.transform 就是获取物体的位置组件
            scriptScopeTable.Set("self", this);
            //Z：将编辑器中配置的物体传递给 Lua，比如injection.name="enemy"则 Lua 中可用enemy变量访问该物体
            foreach (var injection in injections)
            {
                scriptScopeTable.Set(injection.name, injection.value);
            }

            // 如果你希望在脚本内能够设置全局变量, 也可以直接将全局脚本域注入到当前脚本的脚本域中
            // 这样, 你就可以在 Lua 脚本中通过 Global.XXX 来访问全局变量
            // scriptScopeTable.Set("Global", luaEnv.Global);

            // 执行脚本 [Z：执行 Lua 脚本的核心操作，作用是 “把 Lua 脚本的内容跑起来，并让脚本里的变量和函数都存到指定的‘小房间’里”]
            luaEnv.DoString(luaScript.text, luaScript.name, scriptScopeTable);

            // 从 Lua 脚本域中获取定义的函数 [Z：在CSharp中用委托来获取接收Lua中的函数]
            Action luaAwake = scriptScopeTable.Get<Action>("awake");
            scriptScopeTable.Get("start", out luaStart);
            scriptScopeTable.Get("update", out luaUpdate);
            scriptScopeTable.Get("ondestroy", out luaOnDestroy);
            scriptScopeTable.Get("onCollisionEnter2D", out onCollisionEnter2D);
            scriptScopeTable.Get("onTriggerEnter2D", out onTriggerEnter2D);

            if (luaAwake != null)
            {
                luaAwake();
            }
        }
        
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (onCollisionEnter2D != null)
            {
                onCollisionEnter2D(collision);
            }
        }

        private void OnTriggerEnter2D(Collider2D collision)
        {
            if (onTriggerEnter2D != null)
            {
                onTriggerEnter2D(collision);
            }
        }

        // Use this for initialization
        void Start()
        {
            if (luaStart != null)
            {
                luaStart();
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (luaUpdate != null)
            {
                luaUpdate();
            }

            if (Time.time - LuaBehaviour.lastGCTime > GCInterval)
            {
                luaEnv.Tick();
                LuaBehaviour.lastGCTime = Time.time;
            }
        }

        void OnDestroy()
        {
            if (luaOnDestroy != null)
            {
                luaOnDestroy();
            }

            scriptScopeTable.Dispose();
            luaOnDestroy = null;
            luaUpdate = null;
            luaStart = null;
            injections = null;
        }
    }
}
