(function(){
  var out = [];
  out.push("title=" + document.title);
  var b = document.body;
  out.push("bodyChildren=" + b.children.length);
  for (var i=0;i<b.children.length && i<12;i++){
    var c=b.children[i];
    out.push("  <"+c.tagName.toLowerCase()+"> id="+(c.id||"-")+" class="+((c.className&&c.className.baseVal!==undefined?c.className.baseVal:c.className)||"-"));
  }
  out.push("allElements=" + document.getElementsByTagName("*").length);
  return out.join("\n");
})();
