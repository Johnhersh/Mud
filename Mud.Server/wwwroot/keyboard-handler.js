// keyboard-handler.js - Document-level keyboard capture for Blazor interop

let keyboardDotNetRef = null;
let registeredKeyCodes = new Set();

function handleDocumentKeyDown(e) {
    if (registeredKeyCodes.has(e.code)) {
        e.preventDefault();
        keyboardDotNetRef?.invokeMethodAsync('OnKeyDown', e.code);
    }
}

window.registerKeyboardHandler = function(dotNetRef, keyCodes) {
    // Clean up any existing handler first to prevent listener accumulation
    if (keyboardDotNetRef) {
        document.removeEventListener('keydown', handleDocumentKeyDown);
    }
    keyboardDotNetRef = dotNetRef;
    registeredKeyCodes = new Set(keyCodes);
    document.addEventListener('keydown', handleDocumentKeyDown);
};

window.unregisterKeyboardHandler = function() {
    document.removeEventListener('keydown', handleDocumentKeyDown);
    keyboardDotNetRef = null;
    registeredKeyCodes.clear();
};
