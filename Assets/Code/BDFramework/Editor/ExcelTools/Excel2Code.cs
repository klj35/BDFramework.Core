﻿using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BDFramework.ResourceMgr;
using LitJson;
using UnityEditor;
using UnityEngine;
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;


namespace BDFramework.Editor.TableData
{
    static public class Excel2Code
    {
        [MenuItem("BDFrameWork工具箱/3.表格/表格->生成Class", false,
            (int) BDEditorMenuEnum.BuildPackage_Table_Table2Class)]
        public static void Gen()
        {
            var ret = EditorUtility.DisplayDialog("提示", @"
Excel格式如下:
1.第一行为备注
2.第二行可以自定义字段类型，如没检测到则自动分析字段类型
3.所有表格字段名必须以Id开始，即第二或第三行首列必须是Id", "OK");
            if (ret)
            {
                GenCode();
            }
        }


        /// <summary>
        /// 生成代码
        /// </summary>
        public static void GenCode()
        {
            var tablePath = Path.Combine(Application.dataPath, "Resource/Table");
            DirectoryInfo info = new DirectoryInfo(tablePath);

            foreach (var file in info.GetFiles())
            {
                if (!file.FullName.ToLower().EndsWith("xlsx") && !file.FullName.ToLower().EndsWith("xls"))
                    continue;
                string fname = Path.GetFileNameWithoutExtension(file.FullName).ToLower();
                fname = UpperFirst(fname);
                string destPath = Path.GetDirectoryName(file.FullName) + "\\" + fname +
                                  Path.GetExtension(file.FullName);
//                //判断是否重名
                string oldPath = "Assets" + file.FullName.Replace('\\', '/').Replace(Application.dataPath, "");
                string newPath = "Assets" + destPath.Replace('\\', '/').Replace(Application.dataPath, "");
                if (!oldPath.Equals(newPath))
                    AssetDatabase.CopyAsset(oldPath, newPath);
            }

            AssetDatabase.Refresh();

            var tableDir = Path.GetDirectoryName(tablePath);
            var xlslFiles = Directory.GetFiles(tablePath, "*.xlsx", SearchOption.AllDirectories);

            if (xlslFiles.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "未发现xlsx文件，请注意不是xls", "确定");
                return;
            }

            foreach (var f in xlslFiles)
            {
                var excel = new ExcelUtility(f);
                string json = excel.GetJson();
                List<object> statements = excel.GetLine(0);
                //这里将前三列进行判断
                //如果第二列第一行是"Id"，则用自动推测字段模式
                //如果第三列第一行是"Id",则用自定义字段类型模式
                var list = excel.GetLine(1);
                var list2 = excel.GetLine(2);
                if (list[0].Equals("Id"))
                {
                    Json2Class(f, json, statements, null);
                    Debug.Log("[自动分析]导出：" + f);
                }
                else if (list2[0].Equals("Id"))
                {
                    List<object> fieldTypes = excel.GetLine(1);
                    Json2Class(f, json, statements, fieldTypes);
                    Debug.Log("[自定义字段]导出：" + f);
                }
                else
                {
                    Debug.LogError("不符合规范内容:"+f);
                }
                
            }

            EditorUtility.DisplayDialog("提示", "生成完成!", "确定");
            AssetDatabase.Refresh();
        }


        private static string UpperFirst(string s)
        {
            return Regex.Replace(s, @"\b[a-z]\w+", delegate(Match match)
            {
                string v = match.ToString();
                return char.ToUpper(v[0]) + v.Substring(1);
            });
        }

        /// <summary>
        /// Json2Class
        /// 自动分析字段
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="json"></param>
        /// <param name="statements"></param>
        private static void Json2Class(string fileName,
            string json,
            List<object> statements,
            List<object> fieldTypes = null)
        {
            string clsName = "";
            clsName = Path.GetFileName(fileName).ToLower().Replace(".xlsx", "");
            //首字母大写
            clsName = clsName.Substring(0, 1).ToUpper() + clsName.Substring(1);
            //输出目录控制
            string outputFile = Path.Combine(Application.dataPath, "Code/Game@hotfix/Table");
            if (Directory.Exists(outputFile) == false)
            {
                Directory.CreateDirectory(outputFile);
            }

            //输出目录
            outputFile = Path.Combine(outputFile, Path.GetFileName(fileName).Replace(".xlsx", ".cs"));


            //生成类服务
            CodeCompileUnit compunit = new CodeCompileUnit();
            CodeNamespace sample = new CodeNamespace("Game.Data");
            compunit.Namespaces.Add(sample);
            //引用命名空间
            sample.Imports.Add(new CodeNamespaceImport("System"));
            sample.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            sample.Imports.Add(new CodeNamespaceImport("SQLite4Unity3d"));
            //在命名空间下添加一个类
            CodeTypeDeclaration wrapProxyClass = new CodeTypeDeclaration(clsName);
            wrapProxyClass.IsClass = true;
            wrapProxyClass.IsEnum = false;
            wrapProxyClass.IsInterface = false;
            wrapProxyClass.IsPartial = false;
            wrapProxyClass.IsStruct = false;
            //把这个类添加到命名空间 
            sample.Types.Add(wrapProxyClass);

            CodeAttributeDeclaration attr = new CodeAttributeDeclaration("Serializable");
            wrapProxyClass.CustomAttributes.Add(attr);
            //
            var jsonData = JsonMapper.ToObject(json)[0];
            int i = 0;
            foreach (var key in jsonData.Keys)
            {
                //字段

                string memberContent =
                    @"       public [type] [Name] {get;set;}";
                CodeSnippetTypeMember member = new CodeSnippetTypeMember();
                if (key.ToLower() == "id" && key != "Id")
                {
                    Debug.LogErrorFormat("<color=yellow>表格{0}字段必须为Id[大小写区分],请修改后生成</color>", clsName);
                    break;
                }
                else if (key == "Id")
                {
                    //增加一个sqlite主键
                    //member.CustomAttributes.Add(new CodeAttributeDeclaration("PrimaryKey"));
                    memberContent =
                        @"      [PrimaryKey] 
        public [type] [Name] {get;set;}";
                }

                string type = null;
                if (fieldTypes != null && fieldTypes.Count >= jsonData.Count)
                {
                    type = fieldTypes[i].ToString();
                }
                else
                {
                    //自动推测字段类型
                    var value = jsonData[key];
                    if (value.IsArray)
                    {
                        var str = value.ToJson();
                        if (str.IndexOf("\"") > 0)
                        {
                            type = "List<string>";
                        }
                        else
                        {
                            type = "List<double>";
                        }
                    }
                    else if (value.IsInt)
                        type = "int";
                    else if (value.IsDouble || value.IsLong)
                        type = "double";
                    else if (value.IsBoolean)
                        type = "bool";
                    else if (value.IsString)
                        type = "string";
                }


                //注释
                member.Comments.Add(new CodeCommentStatement(statements[i].ToString()));

                member.Text = memberContent.Replace("[type]", type).Replace("[Name]", key);


                wrapProxyClass.Members.Add(member);
                i++;
            }

            //生成代码       
            CodeDomProvider provider = CodeDomProvider.CreateProvider("CSharp");
            CodeGeneratorOptions options = new CodeGeneratorOptions();
            options.BracingStyle = "C";
            options.BlankLinesBetweenMembers = true;

            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(outputFile))
            {
                provider.GenerateCodeFromCompileUnit(compunit, sw, options);
            }
        }

        [MenuItem("Assets/单个excel生成class")]
        public static void SingleExcel2Class()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            FileInfo file = new FileInfo(path);
            string fname = Path.GetFileNameWithoutExtension(file.FullName).ToLower();
            fname = UpperFirst(fname);
            string destPath = Path.GetDirectoryName(file.FullName) + "\\" + fname +
                              Path.GetExtension(file.FullName);
            //                //判断是否重名
            string oldPath = "Assets" + file.FullName.Replace('\\', '/').Replace(Application.dataPath, "");
            string newPath = "Assets" + destPath.Replace('\\', '/').Replace(Application.dataPath, "");
            if (!oldPath.Equals(newPath))
                AssetDatabase.CopyAsset(oldPath, newPath);
            AssetDatabase.Refresh();

            string f = file.FullName;
            var excel = new ExcelUtility(f);
            var json = excel.GetJson();
            var statements = excel.GetLine(0);
            Json2Class(f, json, statements);
            Debug.Log("导出：" + f);

            EditorUtility.DisplayDialog("提示", "生成完成!", "确定");
            AssetDatabase.Refresh();
        }

        [MenuItem("Assets/单个excel生成class", true)]
        private static bool SingleExcel2ClassValidation()
        {
            if (Selection.activeObject == null)
                return false;
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!path.StartsWith("Assets/Resource/Table") || !path.EndsWith(".xlsx"))
                return false;
            return true;
        }
    }
}