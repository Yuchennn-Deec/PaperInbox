var controller, bridge, scope;

async function startup({id, version, rootURI}) {
  const timers = ChromeUtils.importESModule('resource://gre/modules/Timer.sys.mjs');
  scope = {Zotero, Services, Cc, Ci, ChromeUtils, IOUtils, PathUtils,
    setInterval: timers.setInterval, clearInterval: timers.clearInterval};
  Services.scriptloader.loadSubScript(rootURI + 'core.js', scope);
  Services.scriptloader.loadSubScript(rootURI + 'controller.js', scope);
  Services.scriptloader.loadSubScript(rootURI + 'bridge.js', scope);
  controller = new scope.PaperInboxController({id, version, rootURI, bridgeMode: true});
  await controller.start();
  bridge = new scope.PaperInboxBridge(controller, version);
  await bridge.start();
  Zotero.PaperInboxBridge = bridge;
}

async function shutdown() {
  if (bridge) bridge.stop();
  if (controller) await controller.stop();
  if (Zotero.PaperInboxBridge === bridge) delete Zotero.PaperInboxBridge;
  bridge = null; controller = null; scope = null;
}
function install() {}
function uninstall() {}
