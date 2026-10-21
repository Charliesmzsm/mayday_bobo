using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MojoCarrot.Installation {
    internal static class InstallPaths {
        public static readonly string Home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","MojoCarrotDesktop");
        public static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"MojoCarrotDesktop");
        public static readonly string DesktopLink=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Mojo 萝卜桌宠.lnk");
        public static readonly string StartFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Mojo 萝卜桌宠");
        public const string RegistryPath=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MojoCarrotDesktop";
        public static readonly string[] OwnedFiles={"MojoCarrot.exe","Uninstall.exe","使用说明.txt"};
        public static void ClosePet(){
            foreach(Process p in Process.GetProcessesByName("MojoCarrot"))using(p){
                try{
                    if(!String.Equals(p.MainModule.FileName,Path.Combine(Home,"MojoCarrot.exe"),StringComparison.OrdinalIgnoreCase))continue;
                    // Tool windows have no Process.MainWindowHandle. Enumerate this process's windows.
                    uint id=(uint)p.Id;
                    EnumWindows(delegate(IntPtr h,IntPtr l){uint pid;GetWindowThreadProcessId(h,out pid);if(pid==id)PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);return true;},IntPtr.Zero);
                    if(!p.WaitForExit(5000))throw new IOException("请先从托盘退出 Mojo 萝卜桌宠，再重试。");
                }catch(System.ComponentModel.Win32Exception){throw new IOException("无法关闭运行中的桌宠，请从托盘退出后重试。");}
            }
        }
        delegate bool EnumCallback(IntPtr window,IntPtr param);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumCallback callback,IntPtr param);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint id);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,int msg,IntPtr w,IntPtr l);
        public static void Shortcut(string link,string target,string args,string description){
            string parent=Path.GetDirectoryName(link);Directory.CreateDirectory(parent);
            Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=null,shortcut=null;
            try{
                shell=Activator.CreateInstance(type);
                shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
                Type st=shortcut.GetType();
                st.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{target});
                st.InvokeMember("Arguments",BindingFlags.SetProperty,null,shortcut,new object[]{args});
                st.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Path.GetDirectoryName(target)});
                st.InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{description});
                st.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
            }finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        }
        public static string ShortcutTarget(string link){
            Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=null,shortcut=null;
            try{shell=Activator.CreateInstance(type);shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});return (string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);}
            finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        }
        public static void RemoveShortcut(string link,string expected){if(File.Exists(link)&&String.Equals(Path.GetFullPath(ShortcutTarget(link)),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase))File.Delete(link);}
        public static void RemoveOwned(string root){foreach(string name in OwnedFiles){string f=Path.Combine(root,name);if(File.Exists(f))File.Delete(f);}if(Directory.Exists(root)&&Directory.GetFileSystemEntries(root).Length==0)Directory.Delete(root);}
        public static void Unregister(){
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))key.DeleteValue("MojoCarrotDesktop",false);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath,false);
            RemoveShortcut(DesktopLink,Path.Combine(Home,"MojoCarrot.exe"));
            RemoveShortcut(Path.Combine(StartFolder,"Mojo 萝卜桌宠.lnk"),Path.Combine(Home,"MojoCarrot.exe"));
            RemoveShortcut(Path.Combine(StartFolder,"卸载 Mojo 萝卜桌宠.lnk"),Path.Combine(Home,"Uninstall.exe"));
            if(Directory.Exists(StartFolder)&&Directory.GetFileSystemEntries(StartFolder).Length==0)Directory.Delete(StartFolder);
        }
        public static string Hash(string file){using(SHA256 sha=SHA256.Create())using(FileStream s=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    }
}
