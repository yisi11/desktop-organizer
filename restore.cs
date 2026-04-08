using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text;

class DesktopRestore {
    // This structure represents a 'List View Item' in Windows. 
    // We use this to tell Windows which icon we are talking about.
    [StructLayout(LayoutKind.Sequential)]
    struct LVITEM { 
        public uint m;    // Mask (tells Windows which fields are valid)
        public int i;    // Index of the item
        public int s;    // Sub-item index
        public uint st;   // State
        public uint sm;   // State Mask
        public IntPtr t;  // Pointer to the text buffer (The icon name)
        public int c;    // Size of the text buffer
        public int im;   // Image index
        public IntPtr l;  // User-defined parameter
    }

    // --- Win32 API Imports ---
    // These allow C# to call the deep, low-level functions built into Windows (C++ based).
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cl, string w);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, int w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a, bool i, uint p);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr a, uint s, uint t, uint pr);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr h, IntPtr a, uint s, uint t);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, uint s, out IntPtr w);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, uint s, out IntPtr r);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static void Main(string[] args) {
        // Defines where the text file will be saved (same folder as the .exe)
        string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop_layout.txt");
        
        // Step 1: Find the Desktop window handle
        IntPtr h = GetH();
        if (h == IntPtr.Zero) return;

        // Step 2: Decide whether to save or restore based on what you typed
        if (args.Length > 0 && args[0] == "save") Save(h, f);
        else if (args.Length > 0 && args[0] == "restore") Res(h, f);
    }

    // This function hunts through Windows layers to find the "SysListView32"
    // which is the actual 'grid' that holds your desktop icons.
    static IntPtr GetH() {
        IntPtr h = FindWindow("Progman", null);
        h = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
        h = FindWindowEx(h, IntPtr.Zero, "SysListView32", null);
        if (h == IntPtr.Zero) {
            IntPtr w = IntPtr.Zero;
            // Sometimes the desktop is hidden inside a "WorkerW" window instead
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero) {
                h = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (h != IntPtr.Zero) break;
            }
            h = FindWindowEx(h, IntPtr.Zero, "SysListView32", null);
        }
        return h;
    }

    static void Save(IntPtr h, string f) {
        // Ask the desktop: "How many icons do you have?" (0x1004 = LVM_GETITEMCOUNT)
        int c = (int)SendMessage(h, 0x1004, 0, IntPtr.Zero);
        
        uint pid; GetWindowThreadProcessId(h, out pid);
        
        // Open explorer.exe so we can touch its memory
        IntPtr hp = OpenProcess(0x0038, false, pid); 
        
        // IMPORTANT: We allocate 4KB of memory INSIDE Explorer's process.
        // We can't use our own memory because Explorer can't see it.
        IntPtr m = VirtualAllocEx(hp, IntPtr.Zero, 4096, 0x1000, 0x04);
        
        List<string> l = new List<string>();
        for (int i = 0; i < c; i++) {
            IntPtr d; 
            // Ask for icon position (X, Y)
            SendMessage(h, 0x1010, i, m);
            byte[] b = new byte[8]; 
            ReadProcessMemory(hp, m, b, 8, out d); // Copy X,Y from Explorer back to us

            // Prepare a request for the icon's name
            LVITEM vi = new LVITEM(); 
            vi.i = i; vi.m = 1; vi.c = 260; 
            vi.t = (IntPtr)((long)m + 1024); // Put the text 1024 bytes into our shared memory
            
            byte[] vb = S2B(vi); 
            WriteProcessMemory(hp, m, vb, (uint)vb.Length, out d);
            SendMessage(h, 0x1073, i, m); // Ask for the name (LVM_GETITEMTEXT)

            byte[] nb = new byte[520]; 
            ReadProcessMemory(hp, (IntPtr)((long)m + 1024), nb, 520, out d);
            
            // Format: "FileName|X|Y"
            string name = Encoding.Unicode.GetString(nb).Split('\0')[0];
            l.Add(name + "|" + BitConverter.ToInt32(b, 0) + "|" + BitConverter.ToInt32(b, 4));
        }
        // Write everything to the text file
        File.WriteAllLines(f, l.ToArray());
        
        // Clean up: give the memory back to Explorer and close the connection
        VirtualFreeEx(hp, m, 0, 0x8000); CloseHandle(hp);
        Console.WriteLine("Saved.");
    }

    static void Res(IntPtr h, string f) {
        if (!File.Exists(f)) return;
        string[] d = File.ReadAllLines(f);
        int c = (int)SendMessage(h, 0x1004, 0, IntPtr.Zero);
        
        for (int i = 0; i < c; i++) {
            string n = GetN(h, i); // Get current name of icon at index 'i'
            foreach (string s in d) {
                string[] p = s.Split('|');
                // If the name in our file matches the name on the desktop...
                if (p.Length == 3 && p[0] == n) {
                    int x = int.Parse(p[1]);
                    int y = int.Parse(p[2]);
                    // Pack X and Y into a single 64-bit value
                    IntPtr lp = (IntPtr)((y << 16) | (x & 0xffff));
                    // Command Explorer to move the icon! (0x100F = LVM_SETITEMPOSITION)
                    SendMessage(h, 0x100F, i, lp);
                }
            }
        }
        Console.WriteLine("Restored.");
    }

    // Helper to get an icon's name by its index
    static string GetN(IntPtr h, int i) {
        uint pid; GetWindowThreadProcessId(h, out pid);
        IntPtr hp = OpenProcess(0x0038, false, pid);
        IntPtr m = VirtualAllocEx(hp, IntPtr.Zero, 4096, 0x1000, 0x04);
        LVITEM vi = new LVITEM(); vi.i = i; vi.m = 1; vi.c = 260; vi.t = (IntPtr)((long)m + 1024);
        IntPtr d; WriteProcessMemory(hp, m, S2B(vi), (uint)Marshal.SizeOf(vi), out d);
        SendMessage(h, 0x1073, i, m);
        byte[] b = new byte[520]; ReadProcessMemory(hp, (IntPtr)((long)m + 1024), b, 520, out d);
        VirtualFreeEx(hp, m, 0, 0x8000); CloseHandle(hp);
        return Encoding.Unicode.GetString(b).Split('\0')[0];
    }

    // Helper: Turns a C# 'Object' (like our LVITEM) into a byte array
    // so it can be injected into the other process's memory.
    static byte[] S2B(object o) {
        int s = Marshal.SizeOf(o); byte[] a = new byte[s]; IntPtr p = Marshal.AllocHGlobal(s);
        Marshal.StructureToPtr(o, p, true); Marshal.Copy(p, a, 0, s); Marshal.FreeHGlobal(p);
        return a;
    }
}