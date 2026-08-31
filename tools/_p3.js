(function(){
  function vis(el){ var s=getComputedStyle(el); if(s.display==="none"||s.visibility==="hidden") return false; var r=el.getBoundingClientRect(); return r.width>0&&r.height>0; }
  var cas=document.querySelector(".cas-display");
  if(!cas) return "NO CAS";
  var out=["cas children="+cas.children.length];
  var rows=cas.querySelectorAll(".annunciation");
  out.push("annunciation nodes="+rows.length);
  var shown=0;
  for(var i=0;i<rows.length;i++){
    var r=rows[i];
    var t=(r.textContent||"").trim();
    var cn=(r.className&&r.className.baseVal!==undefined?r.className.baseVal:r.className)||"";
    if(vis(r)&&t){ shown++; out.push("  V ["+cn+"] "+t); }
  }
  out.push("visible+nonempty="+shown);
  // divider + warnings block
  var w=document.querySelector(".warnings-display");
  if(w){ out.push("warnings-display vis="+vis(w)+" text="+JSON.stringify((w.textContent||"").trim().substring(0,80))); }
  return out.join("\n");
})();
