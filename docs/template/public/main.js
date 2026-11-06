const languages = [
  { code: 'es', label: 'Español' },
  { code: 'en', label: 'English' }
];

const globeIcon =
  '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" '
  + 'stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
  + '<circle cx="12" cy="12" r="9"/><path d="M3 12h18"/>'
  + '<path d="M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9s1.3-6.4 3.8-9z"/></svg>';

const diagramPalettes = {
  light: {
    background: 'transparent',
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
    fontSize: '14px',
    primaryColor: '#f4eeff',
    primaryBorderColor: '#8e5ce6',
    primaryTextColor: '#212529',
    secondaryColor: '#ffffff',
    secondaryBorderColor: '#cdb8f2',
    secondaryTextColor: '#212529',
    tertiaryColor: '#f8f9fa',
    tertiaryBorderColor: '#dee2e6',
    tertiaryTextColor: '#212529',
    lineColor: '#9a86c4',
    textColor: '#212529',
    edgeLabelBackground: '#ffffff',
    clusterBkg: '#fbf9ff',
    clusterBorder: '#ddd0f5',
    titleColor: '#6a2bd9',
    actorBkg: '#f4eeff',
    actorBorder: '#8e5ce6',
    actorTextColor: '#212529',
    actorLineColor: '#cdb8f2',
    signalColor: '#6c5a94',
    signalTextColor: '#212529',
    labelBoxBkgColor: '#f4eeff',
    labelBoxBorderColor: '#8e5ce6',
    labelTextColor: '#212529',
    loopTextColor: '#6a2bd9',
    noteBkgColor: '#f8f9fa',
    noteBorderColor: '#ced4da',
    noteTextColor: '#212529',
    activationBkgColor: '#ece2ff',
    activationBorderColor: '#8e5ce6',
    ok: '#e7f6ec', okBorder: '#3f9a5b',
    fail: '#fdecee', failBorder: '#d1495b',
    io: '#f8f9fa', ioBorder: '#adb5bd'
  },
  dark: {
    background: 'transparent',
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
    fontSize: '14px',
    primaryColor: '#2b2342',
    primaryBorderColor: '#a97cf0',
    primaryTextColor: '#e9e4f5',
    secondaryColor: '#212529',
    secondaryBorderColor: '#5b4a85',
    secondaryTextColor: '#e9e4f5',
    tertiaryColor: '#2b3035',
    tertiaryBorderColor: '#495057',
    tertiaryTextColor: '#e9e4f5',
    lineColor: '#8c78b8',
    textColor: '#dee2e6',
    edgeLabelBackground: '#212529',
    clusterBkg: '#1d1a26',
    clusterBorder: '#3e3359',
    titleColor: '#c9a6ff',
    actorBkg: '#2b2342',
    actorBorder: '#a97cf0',
    actorTextColor: '#e9e4f5',
    actorLineColor: '#5b4a85',
    signalColor: '#b8a6dc',
    signalTextColor: '#dee2e6',
    labelBoxBkgColor: '#2b2342',
    labelBoxBorderColor: '#a97cf0',
    labelTextColor: '#e9e4f5',
    loopTextColor: '#c9a6ff',
    noteBkgColor: '#2b3035',
    noteBorderColor: '#495057',
    noteTextColor: '#dee2e6',
    activationBkgColor: '#3a2f57',
    activationBorderColor: '#a97cf0',
    ok: '#1f3527', okBorder: '#5dbb7a',
    fail: '#3d2328', failBorder: '#e06c7d',
    io: '#2b3035', ioBorder: '#6c757d'
  }
};

function diagramOptions() {
  const theme = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
  const p = diagramPalettes[theme];
  const { ok, okBorder, fail, failBorder, io, ioBorder, ...themeVariables } = p;
  const themeCSS = [
    '.node rect, .node polygon, .node circle, .node path { stroke-width: 1.4px; }',
    '.node rect.basic, .node rect.label-container { rx: 8px; ry: 8px; }',
    '.cluster rect { rx: 10px; ry: 10px; stroke-width: 1px; }',
    '.edgePath .path, .flowchart-link { stroke-width: 1.4px; }',
    '.edgeLabel, .edgeLabel p { font-size: 12.5px; }',
    '.cluster-label .nodeLabel, .cluster-label span { font-weight: 600; letter-spacing: 0.02em; }',
    '.labelBox, .labelText, .labelText > tspan { display: none; }',
    `.loopLine { stroke: ${themeVariables.labelBoxBorderColor} !important; stroke-width: 1px !important; stroke-dasharray: 4 4 !important; opacity: 0.55; }`,
    '.loopText, .loopText > tspan, .sectionTitle, .sectionTitle > tspan { font-weight: 600 !important; }',
    '.note { rx: 6px; ry: 6px; }',
    `.ok rect, .ok polygon, .ok path { fill: ${ok} !important; stroke: ${okBorder} !important; }`,
    `.fail rect, .fail polygon, .fail path { fill: ${fail} !important; stroke: ${failBorder} !important; }`,
    `.io rect, .io polygon, .io path { fill: ${io} !important; stroke: ${ioBorder} !important; stroke-dasharray: 4 3; }`
  ].join('\n');
  return {
    theme: 'base',
    themeVariables,
    themeCSS,
    flowchart: { curve: 'basis', nodeSpacing: 36, rankSpacing: 48, padding: 14, useMaxWidth: true },
    sequence: { useMaxWidth: true, mirrorActors: false, actorMargin: 60, boxMargin: 8, noteMargin: 10, messageMargin: 36 }
  };
}

function counterpart() {
  const path = window.location.pathname;
  for (const current of languages) {
    const segment = '/' + current.code + '/';
    const index = path.lastIndexOf(segment);
    if (index < 0) {
      continue;
    }

    const other = languages.find(language => language.code !== current.code);
    const href = path.slice(0, index) + '/' + other.code + '/' + path.slice(index + segment.length);
    return { current, other, href };
  }

  return null;
}

function remember(code) {
  try {
    localStorage.setItem('docs-language', code);
  } catch (e) {
  }
}

function addLanguageSwitch() {
  const target = counterpart();
  if (!target) {
    return;
  }

  remember(target.current.code);

  const link = document.createElement('a');
  link.href = target.href;
  link.lang = target.other.code;
  link.className = 'btn border-0 language-switch';
  link.title = target.other.label;
  link.setAttribute('aria-label', target.other.label);
  link.style.display = 'inline-flex';
  link.style.alignItems = 'center';
  link.style.gap = '0.35rem';
  link.innerHTML = globeIcon + '<span style="font-size:0.8rem;font-weight:600;letter-spacing:0.03em">'
    + target.other.code.toUpperCase() + '</span>';
  link.addEventListener('click', () => remember(target.other.code));

  const icons = document.querySelector('header form.icons') || document.querySelector('header .icons');
  if (icons) {
    icons.prepend(link);
    return;
  }

  const container = document.querySelector('header nav .container-xxl') || document.querySelector('header nav');
  if (container) {
    container.append(link);
    return;
  }

  link.style.position = 'fixed';
  link.style.top = '0.5rem';
  link.style.right = '1rem';
  link.style.zIndex = '1000';
  document.body.append(link);
}

export default {
  start: addLanguageSwitch,
  get mermaid() {
    return diagramOptions();
  }
};
