(function(){
  function vis(el){ if(!el) return false; var s=getComputedStyle(el); if(s.display==="none"||s.visibility==="hidden") return false; var r=el.getBoundingClientRect(); return r.width>0&&r.height>0; }
  function txt(sel){ var e=document.querySelector(sel); return e? JSON.stringify((e.textContent||"").trim().substring(0,60))+(vis(e)?" [V]":" [hidden]") : "MISSING"; }
  var probes = {
    "ias box":            ".airspeed-ias-box-scrollers",
    "alt box":            ".altimeter-altitude-box",
    "alt scrollers":      ".altimeter-altitude-scroller",
    "baro":               ".altimeter-baro-value",
    "baro container":     ".altimeter-baro",
    "vsi":                ".vsi-value",
    "hdg readout":        ".hsi-headingbug-value",
    "compass":            "#HSI",
    "fma":                ".fma",
    "minimums":           ".altimeter-minimums",
    "nav status":         ".nav-status",
    "wind":               ".wind-data"
  };
  var out=[];
  for(var k in probes) out.push(k.padEnd(18)+" "+probes[k].padEnd(34)+" "+txt(probes[k]));
  // any class containing these words, to find the real names
  var want=["fma","vsi","altimeter-","hsi-","wind","minimum","nav-status","softkey"];
  var found={};
  var all=document.getElementsByTagName("*");
  for(var i=0;i<all.length;i++){
    var cn=(all[i].className&&all[i].className.baseVal!==undefined?all[i].className.baseVal:all[i].className)||"";
    if(typeof cn!=="string")continue;
    cn.split(/\s+/).forEach(function(c){
      for(var j=0;j<want.length;j++) if(c.indexOf(want[j])===0||c===want[j]) found[c]=1;
    });
  }
  out.push("--- classes seen ---");
  out.push(Object.keys(found).sort().join(" "));
  return out.join("\n");
})();
