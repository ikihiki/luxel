import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';

let renderQueue = Promise.resolve();

async function render(root = document) {
    const nodes = [...root.querySelectorAll('pre.mermaid')];
    if (nodes.length === 0) return;
    for (const node of nodes) {
        node.dataset.mermaidSource ||= node.textContent || '';
        node.textContent = node.dataset.mermaidSource;
        node.removeAttribute('data-processed');
    }
    mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: document.documentElement.dataset.theme === 'light' ? 'default' : 'dark',
    });
    await mermaid.run({ nodes });
}

window.LuxelMermaid = {
    render(root = document) {
        renderQueue = renderQueue.then(() => render(root)).catch(error => {
            console.error('Mermaid rendering failed.', error);
        });
        return renderQueue;
    },
};

window.LuxelMermaid.render(document);