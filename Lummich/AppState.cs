using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lummich.Models;
public static class AppState {
    public static List<PhotoItem> Photos { get; set; }
    public static int CurrentIndex { get; set; }
}
