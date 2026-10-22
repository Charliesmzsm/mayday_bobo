using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
[assembly: AssemblyTitle("Mojo 萝卜桌宠安装程序")]
[assembly: AssemblyCompany("Mojo Carrot")]
[assembly: AssemblyProduct("Mojo 萝卜桌宠")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]
namespace MojoCarrot.Installation {
    static class SetupProgram {
        [STAThread]static int Main(string[] args){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{
                if(args.Length==2&&args[0]=="--verify"){Payload.Extract(args[1]);File.WriteAllText(Path.Combine(args[1],"verification.txt"),"PASS: installer payload extracted successfully\r\nMojoCarrot.exe SHA256 "+InstallPaths.Hash(Path.Combine(args[1],"MojoCarrot.exe"))+"\r\nUninstall.exe SHA256 "+InstallPaths.Hash(Path.Combine(args[1],"Uninstall.exe")));return 0;}
                if(args.Length==2&&args[0]=="--test-install")return Payload.Test(args[1]);
                if(args.Length==2&&args[0]=="--ui-preview"){using(SetupForm form=new SetupForm()){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1]);}}return 0;}
                Application.Run(new SetupForm());return 0;
            }catch(Exception ex){if(args.Length>0){try{File.WriteAllText(Path.Combine(args[1],"error.txt"),ex.ToString());}catch{}return 1;}MessageBox.Show("安装程序无法启动：\n"+ex.Message,"Mojo 萝卜",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
    static class Payload {
        public static void Extract(string directory){Directory.CreateDirectory(directory);foreach(string name in InstallPaths.OwnedFiles)using(Stream source=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(FileStream destination=File.Create(Path.Combine(directory,name))){if(source==null)throw new InvalidDataException("安装资源缺失："+name);source.CopyTo(destination);}}
        public static void Install(bool desktop){
            InstallPaths.ClosePet();
            string stage=Path.Combine(Path.GetTempPath(),"MojoCarrot-Setup-"+Guid.NewGuid().ToString("N"));
            Extract(stage);Directory.CreateDirectory(InstallPaths.Home);
            // Preserve the prior version until every replacement and registration succeeds.
            string backup=Path.Combine(stage,"backup");Directory.CreateDirectory(backup);
            foreach(string name in InstallPaths.OwnedFiles){string old=Path.Combine(InstallPaths.Home,name);if(File.Exists(old))File.Copy(old,Path.Combine(backup,name));}
            try{
                foreach(string name in InstallPaths.OwnedFiles)File.Copy(Path.Combine(stage,name),Path.Combine(InstallPaths.Home,name),true);
                InstallPaths.Shortcut(Path.Combine(InstallPaths.StartFolder,"Mojo 萝卜桌宠.lnk"),Path.Combine(InstallPaths.Home,"MojoCarrot.exe"),"","启动独立离线桌宠");
                InstallPaths.Shortcut(Path.Combine(InstallPaths.StartFolder,"卸载 Mojo 萝卜桌宠.lnk"),Path.Combine(InstallPaths.Home,"Uninstall.exe"),"","卸载 Mojo 萝卜桌宠");
                if(desktop)InstallPaths.Shortcut(InstallPaths.DesktopLink,Path.Combine(InstallPaths.Home,"MojoCarrot.exe"),"","Mojo 萝卜桌宠");
                using(RegistryKey key=Registry.CurrentUser.CreateSubKey(InstallPaths.RegistryPath)){
                    key.SetValue("DisplayName","Mojo 萝卜桌宠");key.SetValue("DisplayVersion","1.1.2");key.SetValue("Publisher","Mojo Carrot");key.SetValue("InstallLocation",InstallPaths.Home);key.SetValue("DisplayIcon",Path.Combine(InstallPaths.Home,"MojoCarrot.exe"));key.SetValue("UninstallString","\""+Path.Combine(InstallPaths.Home,"Uninstall.exe")+"\"");key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);key.SetValue("EstimatedSize",checked((int)(new FileInfo(Path.Combine(InstallPaths.Home,"MojoCarrot.exe")).Length/1024+100)),RegistryValueKind.DWord);
                }
            }catch{
                bool previous=File.Exists(Path.Combine(backup,"MojoCarrot.exe"));
                foreach(string name in InstallPaths.OwnedFiles){string old=Path.Combine(backup,name),target=Path.Combine(InstallPaths.Home,name);if(File.Exists(old))File.Copy(old,target,true);else if(File.Exists(target))File.Delete(target);}
                if(!previous)InstallPaths.Unregister();throw;
            }finally{
                // Only remove known setup files. User-created files in any directory are preserved.
                foreach(string name in InstallPaths.OwnedFiles){string a=Path.Combine(stage,name),b=Path.Combine(backup,name);if(File.Exists(a))File.Delete(a);if(File.Exists(b))File.Delete(b);}
                if(Directory.GetFileSystemEntries(backup).Length==0)Directory.Delete(backup);if(Directory.GetFileSystemEntries(stage).Length==0)Directory.Delete(stage);
            }
        }
        public static int Test(string directory){
            Directory.CreateDirectory(directory);string root=Path.Combine(directory,"isolated-install");Extract(root);
            string expected=InstallPaths.Hash(Path.Combine(root,"MojoCarrot.exe"));
            string link=Path.Combine(root,"Mojo-test.lnk");InstallPaths.Shortcut(link,Path.Combine(root,"MojoCarrot.exe"),"","测试");
            if(!String.Equals(InstallPaths.ShortcutTarget(link),Path.Combine(Path.GetFullPath(root),"MojoCarrot.exe"),StringComparison.OrdinalIgnoreCase))throw new Exception("shortcut target mismatch");
            string preserve=Path.Combine(root,"user-file-preserved.txt");File.WriteAllText(preserve,"keep");
            InstallPaths.RemoveShortcut(link,Path.Combine(root,"MojoCarrot.exe"));InstallPaths.RemoveOwned(root);
            if(!File.Exists(preserve)||File.Exists(Path.Combine(root,"MojoCarrot.exe"))||File.Exists(link))throw new Exception("uninstall did not preserve the right files");
            File.WriteAllText(Path.Combine(directory,"installer-test.txt"),"PASS: payload extraction, native Windows shortcut creation and target resolution, uninstall of owned files, preservation of unrelated user files.\r\nPayload SHA256 "+expected+"\r\nSystem install paths and registration were not modified by this test.");return 0;
        }
    }
    sealed class SetupForm:Form {
        readonly CheckBox desktop=new CheckBox(),launch=new CheckBox();readonly Button install=new Button();readonly Label status=new Label();bool completed;
        public SetupForm(){
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Text="安装 Mojo 萝卜桌宠";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(560,390);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("mojo.ico"))Icon=new Icon(s);
            Label title=new Label{Text="Mojo 萝卜桌宠",Font=new Font("Microsoft YaHei UI",22,FontStyle.Bold),AutoSize=true,Location=new Point(28,25)};Controls.Add(title);
            Controls.Add(new Label{Text="一只陪你工作的绒毛小萝卜",AutoSize=true,Location=new Point(31,78)});
            Controls.Add(new Label{Text="本地运行 · 无需账号 · 无需 ChatGPT / Codex",AutoSize=true,Location=new Point(31,111),ForeColor=Color.FromArgb(60,105,57)});
            Controls.Add(new Label{Text="安装位置（当前 Windows 用户）：\n"+InstallPaths.Home,Location=new Point(31,154),Size=new Size(500,56),AutoEllipsis=true});
            desktop.Text="创建桌面快捷方式";desktop.Checked=true;desktop.SetBounds(31,225,280,25);Controls.Add(desktop);
            launch.Text="安装完成后启动桌宠";launch.Checked=true;launch.SetBounds(31,257,300,25);Controls.Add(launch);
            using(Stream payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("MojoCarrot.exe"))
                status.Text="无需管理员权限，约占用 "+(int)Math.Ceiling(payload.Length/1048576.0+1)+" MB 空间。";
            status.SetBounds(31,299,490,32);Controls.Add(status);
            install.Text="安装";install.SetBounds(412,340,115,34);install.Click+=InstallClicked;Controls.Add(install);AcceptButton=install;
            Button cancel=new Button{Text="取消",Location=new Point(288,340),Size=new Size(110,34),DialogResult=DialogResult.Cancel};cancel.Click+=delegate{Close();};Controls.Add(cancel);CancelButton=cancel;
        }
        void InstallClicked(object sender,EventArgs e){
            if(completed){Close();return;}install.Enabled=false;UseWaitCursor=true;status.Text="正在安装…";Refresh();
            try{Payload.Install(desktop.Checked);completed=true;status.Text="安装完成。右键桌宠或托盘图标即可打开菜单。";install.Text="完成";
                if(launch.Checked){try{Process.Start(new ProcessStartInfo(Path.Combine(InstallPaths.Home,"MojoCarrot.exe")){UseShellExecute=true});}catch(Exception ex){MessageBox.Show("安装已完成，但无法启动：\n"+ex.Message+"\n可从桌面或开始菜单重试。","Mojo 萝卜");}}
            }catch(Exception ex){status.Text="安装未完成，可修正后重试。";MessageBox.Show("安装失败：\n"+ex.Message,"Mojo 萝卜",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{UseWaitCursor=false;install.Enabled=true;}
        }
    }
}
