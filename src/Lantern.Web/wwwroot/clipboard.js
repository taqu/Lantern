export async function copyText(text) {
    if (!navigator.clipboard) {
        return { success: false, error: 'Clipboard API not available' };
    }
    try {
        await navigator.clipboard.writeText(text);
        return { success: true, error: null };
    } catch (e) {
        return { success: false, error: e.message || 'Copy failed' };
    }
}
