/* The native browser scales its viewport; keep the original 460 x 315 design. */
(function () {
  function fit() {
    if (!document.body) { return; }
    var root = document.documentElement;
    var width = window.innerWidth || (root && root.clientWidth) || 0;
    var height = window.innerHeight || (root && root.clientHeight) || 0;
    var scale = Math.min(width / 460, height / 315);
    if (isFinite(scale) && scale > 0) {
      document.body.style.zoom = String(scale);
    }
  }
  if (window.addEventListener) {
    window.addEventListener("resize", fit, false);
    window.addEventListener("load", fit, false);
  } else if (window.attachEvent) {
    window.attachEvent("onresize", fit);
    window.attachEvent("onload", fit);
  }
  fit();
}());
