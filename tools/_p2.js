(function(){
  var out=[];
  function vis(el){ var s=getComputedStyle(el); if(s.display==="none"||s.visibility==="hidden"||s.opacity==="0") return false; var r=el.getBoundingClientRect(); return r.width>0&&r.height>0; }
  // candidate containers by class/id keyword
  var keys=["cas","annunc","alert","warning","caution","advisory","airspeed","altimeter","altitude","heading","compass","fma","baro","minimum"];
  var seen={};
  var all=document.getElementsByTagName("*");
  for(var i=0;i<all.length;i++){
    var e=all[i];
    var cn=(e.className&&e.className.baseVal!==undefined?e.className.baseVal:e.className)||"";
    var id=e.id||"";
    var tag=e.tagName.toLowerCase();
    var hay=(cn+" "+id+" "+tag).toLowerCase();
    for(var k=0;k<keys.length;k++){
      if(hay.indexOf(keys[k])>=0){
        var sig=tag+"#"+id+"."+cn;
        if(seen[sig])break; seen[sig]=1;
        out.push((vis(e)?"V ":"- ")+sig.substring(0,90));
        break;
      }
    }
  }
  return out.slice(0,70).join("\n");
})();
