using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Bacheca {
 public class Config { public string endpoint {get;set;} public string token {get;set;} }
 public class Slide { public string id {get;set;} public string title {get;set;} public string sha256 {get;set;} public string url {get;set;} }
 public class Manifest { public string name {get;set;} public int interval_seconds {get;set;} public List<Slide> slides {get;set;} }
 public static class Store {
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Bacheca");
  public static readonly string Cache=Path.Combine(Root,"immagini");
  public static JavaScriptSerializer Json(){return new JavaScriptSerializer { MaxJsonLength=1024*1024 };}
  public static void Init(){Directory.CreateDirectory(Root);Directory.CreateDirectory(Cache);}
  public static void Atomic(string path,byte[] bytes){string temp=path+".tmp";File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public static void SaveConfig(Config c){Validate(c);Atomic(Path.Combine(Root,"config.dat"),ProtectedData.Protect(Encoding.UTF8.GetBytes(Json().Serialize(c)),null,DataProtectionScope.CurrentUser));}
  public static Config LoadConfig(){try{return Json().Deserialize<Config>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Root,"config.dat")),null,DataProtectionScope.CurrentUser)));}catch{return null;}}
  public static void Validate(Config c){Uri u;if(c==null||!Uri.TryCreate(c.endpoint,UriKind.Absolute,out u)||u.Scheme!="https"||!u.Host.EndsWith(".supabase.co",StringComparison.OrdinalIgnoreCase)||u.AbsolutePath!="/functions/v1/screen"||!System.Text.RegularExpressions.Regex.IsMatch(c.token??"","^[a-f0-9]{64}$"))throw new Exception("Configurazione non valida. Copiala dalla sezione Schermi del sito.");}
  public static string ImagePath(Slide s){if(!System.Text.RegularExpressions.Regex.IsMatch(s.sha256??"","^[a-f0-9]{64}$"))throw new Exception("Hash immagine non valido.");return Path.Combine(Cache,s.sha256+".jpg");}
  public static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  public static void Log(string message){try{string p=Path.Combine(Root,"stato.log");if(File.Exists(p)&&new FileInfo(p).Length>65536)File.Delete(p);File.AppendAllText(p,DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}}
 }
 public class Player : Form {
  [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
  readonly PictureBox picture=new PictureBox();
  readonly Label empty=new Label();
  readonly System.Windows.Forms.Timer rotation=new System.Windows.Forms.Timer();
  readonly System.Windows.Forms.Timer syncTimer=new System.Windows.Forms.Timer();
  List<Slide> slides=new List<Slide>();
  Config config; int position=-1; int syncing; bool fullscreen=true;
  public Player(){
   Text="Bacheca";BackColor=Color.Black;KeyPreview=true;FormBorderStyle=FormBorderStyle.None;WindowState=FormWindowState.Maximized;
   picture.Dock=DockStyle.Fill;picture.SizeMode=PictureBoxSizeMode.Zoom;Controls.Add(picture);
   empty.Text="BACHECA\n\nNessun avviso disponibile.\n\nF2  Configura il collegamento\nF3  Importa immagini dal PC\nEsc  Esci";empty.Dock=DockStyle.Fill;empty.TextAlign=ContentAlignment.MiddleCenter;empty.ForeColor=Color.FromArgb(180,200,185);empty.Font=new Font("Segoe UI",18);empty.BackColor=Color.FromArgb(20,35,28);Controls.Add(empty);
   config=Store.LoadConfig();LoadManifest();rotation.Interval=30000;rotation.Tick+=(s,e)=>Next();rotation.Start();syncTimer.Interval=60000;syncTimer.Tick+=(s,e)=>Sync();syncTimer.Start();
   Shown+=(s,e)=>{SetThreadExecutionState(0x80000003);if(slides.Count>0)Next();Sync();if(config==null&&slides.Count==0)Setup();};
   KeyDown+=(s,e)=>{if(e.KeyCode==Keys.F2)Setup();else if(e.KeyCode==Keys.F3)Import();else if(e.KeyCode==Keys.Right)Next();else if(e.KeyCode==Keys.F11)ToggleFullscreen();else if(e.KeyCode==Keys.Escape)Close();};
   FormClosed+=(s,e)=>{SetThreadExecutionState(0x80000000);if(picture.Image!=null)picture.Image.Dispose();};
  }
  void ToggleFullscreen(){fullscreen=!fullscreen;WindowState=FormWindowState.Normal;FormBorderStyle=fullscreen?FormBorderStyle.None:FormBorderStyle.Sizable;if(fullscreen)WindowState=FormWindowState.Maximized;else Size=new Size(900,650);}
  void LoadManifest(){try{var m=Store.Json().Deserialize<Manifest>(File.ReadAllText(Path.Combine(Store.Root,"manifest.json")));if(m.slides!=null)slides=m.slides.FindAll(s=>File.Exists(Store.ImagePath(s)));}catch{} }
  void Next(){if(slides.Count==0){empty.Visible=true;if(picture.Image!=null){var old=picture.Image;picture.Image=null;old.Dispose();}return;}position=(position+1)%slides.Count;try{using(var stream=new MemoryStream(File.ReadAllBytes(Store.ImagePath(slides[position]))))using(var img=Image.FromStream(stream)){var bmp=new Bitmap(img);var old=picture.Image;picture.Image=bmp;if(old!=null)old.Dispose();}empty.Visible=false;}catch(Exception ex){Store.Log("Immagine non leggibile: "+ex.Message);} }
  void Setup(){using(var f=new SetupForm(config)){if(f.ShowDialog(this)==DialogResult.OK){config=f.Result;Sync();}}}
  void Import(){using(var dialog=new OpenFileDialog {Filter="Immagini|*.jpg;*.jpeg;*.png",Multiselect=true,Title="Importa immagini per la prova locale"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(config!=null&&MessageBox.Show(this,"Il collegamento cloud e attivo: gli avvisi locali verranno sostituiti alla prossima sincronizzazione. Continuare?","Prova locale",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{var list=new List<Slide>();foreach(string file in dialog.FileNames){if(new FileInfo(file).Length>15*1024*1024)throw new Exception("File troppo grande: "+Path.GetFileName(file));using(var img=Image.FromFile(file)){if((long)img.Width*img.Height>50000000)throw new Exception("Immagine troppo grande.");double scale=Math.Min(1,Math.Min(1600.0/img.Width,1200.0/img.Height));using(var resized=new Bitmap(Math.Max(1,(int)(img.Width*scale)),Math.Max(1,(int)(img.Height*scale)))){using(var g=Graphics.FromImage(resized)){g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(img,0,0,resized.Width,resized.Height);}using(var ms=new MemoryStream()){resized.Save(ms,System.Drawing.Imaging.ImageFormat.Jpeg);byte[] bytes=ms.ToArray();var slide=new Slide {id=Guid.NewGuid().ToString(),title=Path.GetFileNameWithoutExtension(file),sha256=Store.Hash(bytes)};Store.Atomic(Store.ImagePath(slide),bytes);list.Add(slide);}}}}Commit(list);Store.Log("Importazione locale completata.");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Importazione non riuscita");}}}
  static byte[] Request(string url,string token,int limit){
   var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=20000;request.ReadWriteTimeout=20000;request.AllowAutoRedirect=false;request.UserAgent="Bacheca/1.0";
   if(token!=null){request.Method="POST";request.Headers.Add("X-Device-Token",token);request.ContentLength=0;}
   using(var response=(HttpWebResponse)request.GetResponse())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){byte[] buffer=new byte[16384];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(output.Length+n>limit)throw new Exception("Risposta troppo grande.");output.Write(buffer,0,n);}return output.ToArray();}
  }
  void Commit(List<Slide> list){var m=new Manifest {name="Bacheca",interval_seconds=30,slides=list};Store.Atomic(Path.Combine(Store.Root,"manifest.json"),Encoding.UTF8.GetBytes(Store.Json().Serialize(m)));slides=list;position=-1;Next();rotation.Stop();rotation.Start();}
  void Sync(){var c=config;if(c==null||Interlocked.CompareExchange(ref syncing,1,0)!=0)return;
   ThreadPool.QueueUserWorkItem(delegate{try{
    Store.Validate(c);var bytes=Request(c.endpoint,c.token,1024*1024);var m=Store.Json().Deserialize<Manifest>(Encoding.UTF8.GetString(bytes));if(m==null||m.slides==null||m.slides.Count>500)throw new Exception("Manifest non valido.");
    foreach(var slide in m.slides){string path=Store.ImagePath(slide);if(File.Exists(path)&&Store.Hash(File.ReadAllBytes(path))==slide.sha256)continue;Uri url;if(!Uri.TryCreate(slide.url,UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!=new Uri(c.endpoint).Host)throw new Exception("Origine immagine non valida.");byte[] image=Request(slide.url,null,5*1024*1024);if(Store.Hash(image)!=slide.sha256)throw new Exception("Verifica immagine non riuscita.");using(var stream=new MemoryStream(image))using(var decoded=Image.FromStream(stream)){if(decoded.Width>1600||decoded.Height>1200)throw new Exception("Risoluzione immagine non valida.");}Store.Atomic(path,image);}
    if(!IsDisposed&&IsHandleCreated)Invoke((MethodInvoker)delegate{if(config!=c)return;bool changed=slides.Count!=m.slides.Count;for(int i=0;!changed&&i<slides.Count;i++)changed=slides[i].sha256!=m.slides[i].sha256;if(changed)Commit(m.slides);Cleanup(m.slides);});Store.Log("Sincronizzazione completata: "+m.slides.Count+" avvisi.");
   }catch(WebException ex){string detail=ex.Response is HttpWebResponse?((HttpWebResponse)ex.Response).StatusCode.ToString():ex.Status.ToString();Store.Log("Cloud non disponibile ("+detail+"). Riproduco gli avvisi salvati.");if(ex.Response!=null)ex.Response.Dispose();}catch(Exception ex){Store.Log("Sincronizzazione: "+ex.Message);}finally{Interlocked.Exchange(ref syncing,0);}});
  }
  static void Cleanup(List<Slide> list){try{var keep=new HashSet<string>();foreach(var s in list)keep.Add(Store.ImagePath(s));foreach(string f in Directory.GetFiles(Store.Cache,"*.jpg"))if(!keep.Contains(f))File.Delete(f);}catch{} }
 }
 public class SetupForm : Form {
  public Config Result; readonly TextBox box=new TextBox(); readonly CheckBox auto=new CheckBox(); readonly Config current;
  public SetupForm(Config c){current=c;Text="Configura Bacheca";Size=new Size(570,430);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
   var label=new Label {Text="Dal sito: Schermi > Collega un PC. Incolla qui la configurazione.\nLa credenziale viene protetta per questo utente Windows.",Left=20,Top=18,Width=510,Height=45};Controls.Add(label);box.SetBounds(20,75,510,190);box.Multiline=true;box.ScrollBars=ScrollBars.Vertical;box.Font=new Font("Consolas",10);box.Text=c==null?"":Store.Json().Serialize(c);Controls.Add(box);
   auto.Text="Avvia Bacheca all'accesso a Windows";auto.SetBounds(20,280,450,24);auto.Checked=File.Exists(StartupPath());Controls.Add(auto);
   var save=new Button {Text="Salva e collega",Left=370,Top=325,Width=160,Height=32};save.Click+=(s,e)=>{try{Result=Store.Json().Deserialize<Config>(box.Text);Store.SaveConfig(Result);SetStartup(auto.Checked);DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message,"Controlla la configurazione");}};Controls.Add(save);
   var close=new Button {Text="Annulla",Left=250,Top=325,Width=105,Height=32,DialogResult=DialogResult.Cancel};Controls.Add(close);CancelButton=close;
  }
  static string StartupPath(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Bacheca.cmd");}
  static void SetStartup(bool enabled){if(enabled){string exe=Application.ExecutablePath;if(exe.Contains("%")||exe.Contains("\r")||exe.Contains("\n"))throw new Exception("Sposta il programma in un percorso senza caratteri percentuale o ritorni a capo.");File.WriteAllText(StartupPath(),"@echo off\r\nstart \"\" \""+exe+"\"\r\n",Encoding.Default);}else if(File.Exists(StartupPath()))File.Delete(StartupPath());}
 }
 public static class Program {
  [STAThread] public static void Main(string[] args){
   bool created;using(var mutex=new Mutex(true,"Local\\BachecaPlayer",out created)){if(!created)return;
    ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;Store.Init();
    if(args.Length==1&&args[0]=="--self-test"){SelfTest();return;}
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Player());
   }
  }
  static void SelfTest(){try{byte[] bytes=Encoding.UTF8.GetBytes("abc");if(Store.Hash(bytes)!="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")throw new Exception("SHA256");bool rejected=false;try{Store.Validate(new Config {endpoint="http://bad",token="x"});}catch{rejected=true;}if(!rejected)throw new Exception("Validation");Store.Validate(new Config {endpoint="https://example.supabase.co/functions/v1/screen",token=new string('a',64)});var m=Store.Json().Deserialize<Manifest>("{\"slides\":[],\"interval_seconds\":30}");if(m.slides.Count!=0)throw new Exception("JSON");Store.Log("SELF-TEST PASS");Environment.ExitCode=0;}catch(Exception ex){Store.Log("SELF-TEST FAIL "+ex.Message);Environment.ExitCode=1;}}
 }
}
