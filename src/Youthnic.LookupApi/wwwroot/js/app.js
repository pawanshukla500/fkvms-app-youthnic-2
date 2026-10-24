/**
 * Youthnic VMS & Customer Portal — Client Application
 */

document.addEventListener('DOMContentLoaded', () => {
  // Elements
  const healthPill = document.getElementById('health-pill');
  const healthDot = document.getElementById('health-dot');
  const healthText = document.getElementById('health-text');
  
  const statDb = document.getElementById('stat-db');
  const statMapping = document.getElementById('stat-mapping');
  const statLatency = document.getElementById('stat-latency');
  
  const searchForm = document.getElementById('search-form');
  const trackingInput = document.getElementById('tracking-input');
  const searchSpinner = document.getElementById('search-spinner');
  const btnSearch = document.getElementById('btn-search');
  
  const resultsArea = document.getElementById('results-area');
  const resultAwb = document.getElementById('result-awb');
  const resultSeller = document.getElementById('result-seller');
  const resultStatus = document.getElementById('result-status');
  const itemsContainer = document.getElementById('items-container');
  const resultError = document.getElementById('result-error');
  const resultSuccess = document.getElementById('result-success');
  
  const samplePills = document.querySelectorAll('.sample-pill');
  const marketplaceBtns = document.querySelectorAll('.marketplace-btn');
  
  let currentMarketplace = 'flipkart';

  // 1. Marketplace Switcher
  marketplaceBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      marketplaceBtns.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      currentMarketplace = btn.dataset.marketplace;
      trackingInput.placeholder = currentMarketplace === 'flipkart' 
        ? 'Enter Flipkart Tracking ID / AWB (e.g. FMPP4335992170)...' 
        : 'Enter Myntra Waybill / Tracking ID...';
    });
  });

  // 2. Sample Clickers
  samplePills.forEach(pill => {
    pill.addEventListener('click', () => {
      const sample = pill.dataset.sample;
      trackingInput.value = sample;
      runTracking(sample);
    });
  });

  // 3. Search Form Submit
  searchForm.addEventListener('submit', (e) => {
    e.preventDefault();
    const query = trackingInput.value.trim();
    if (query) {
      runTracking(query);
    }
  });

  // 4. Live Health Check
  async function checkHealth() {
    const startTime = performance.now();
    try {
      const res = await fetch('/health', { cache: 'no-store' });
      const elapsed = Math.round(performance.now() - startTime);
      
      if (!res.ok) throw new Error('Health returned ' + res.status);
      const data = await res.json();
      
      if (data.status === 'ok' && data.database === 'connected') {
        healthText.textContent = `Operational (${elapsed}ms)`;
        healthPill.className = 'status-pill';
        statDb.innerHTML = `<span style="color:var(--status-success)">●</span> Connected`;
        statMapping.innerHTML = `<span style="color:var(--status-success)">●</span> Ready (${data.readOnly ? 'Read-Only' : 'Standard'})`;
        statLatency.textContent = `${elapsed} ms`;
      } else {
        healthText.textContent = 'Degraded';
        healthPill.style.color = 'var(--status-warning)';
        statDb.textContent = data.database || 'Checking';
        statMapping.textContent = data.mapping || 'Pending';
        statLatency.textContent = `${elapsed} ms`;
      }
    } catch (err) {
      healthText.textContent = 'Offline';
      healthPill.style.color = 'var(--status-error)';
      statDb.textContent = 'Disconnected';
      statMapping.textContent = 'Unavailable';
      statLatency.textContent = 'Timeout';
    }
  }

  // Poll health immediately and every 15s
  checkHealth();
  setInterval(checkHealth, 15000);

  // 5. Tracking Lookup
  async function runTracking(trackingId) {
    resultsArea.style.display = 'block';
    resultError.style.display = 'none';
    resultSuccess.style.display = 'none';
    searchSpinner.style.display = 'inline-block';
    btnSearch.disabled = true;

    try {
      // Use public /api/track or /api/lookup
      const url = `/api/track?marketplace=${encodeURIComponent(currentMarketplace)}&trackingId=${encodeURIComponent(trackingId)}`;
      const res = await fetch(url);
      const data = await res.json();

      searchSpinner.style.display = 'none';
      btnSearch.disabled = false;

      if (data.status === 'ok') {
        renderSuccess(data);
      } else if (data.status === 'not_found') {
        renderNotFound(data, trackingId);
      } else if (data.status === 'mapping_pending') {
        renderError('Warehouse Database Synchronizing', 'The Forward Scan schema mapping is currently synchronizing. Please retry in a few moments.');
      } else {
        renderError('Tracking Query Failed', data.detail || 'Could not locate order details.');
      }
    } catch (err) {
      searchSpinner.style.display = 'none';
      btnSearch.disabled = false;
      renderError('Network Error', 'Could not communicate with the tracking service: ' + err.message);
    }
  }

  function renderSuccess(data) {
    resultSuccess.style.display = 'block';
    resultError.style.display = 'none';
    
    resultAwb.textContent = data.trackingId;
    resultSeller.textContent = data.items[0]?.seller ? `Seller Account: ${data.items[0].seller}` : 'Forward Scan Verified Order';
    resultStatus.innerHTML = `<span class="result-status-badge badge-success">✓ Verified & Ready</span>`;

    // Render items
    itemsContainer.innerHTML = '';
    if (data.items && data.items.length > 0) {
      data.items.forEach(item => {
        const row = document.createElement('div');
        row.className = 'item-row';
        
        const hasImg = item.imageUrls && item.imageUrls.length > 0;
        const imgHtml = hasImg 
          ? `<img src="${escapeHtml(item.imageUrls[0])}" alt="SKU Photo" onerror="this.src='/assets/flipkart_icon.png'">`
          : `<svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path><polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline><line x1="12" y1="22.08" x2="12" y2="12"></line></svg>`;

        row.innerHTML = `
          <div class="item-left">
            <div class="item-thumb">${imgHtml}</div>
            <div>
              <div class="item-sku">${escapeHtml(item.sku || 'Unknown SKU')}</div>
              <div class="item-title">${escapeHtml(item.skuName || item.sku || '')}</div>
            </div>
          </div>
          <div class="item-right">
            <span class="qty-badge">Qty: ${item.quantity || 1}</span>
          </div>
        `;
        itemsContainer.appendChild(row);
      });
    } else {
      itemsContainer.innerHTML = `<p style="color:var(--text-muted); padding:12px 0;">No items mapped to this shipment.</p>`;
    }

    resultsArea.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function renderNotFound(data, trackingId) {
    resultError.style.display = 'block';
    resultSuccess.style.display = 'none';
    resultError.innerHTML = `
      <div style="text-align: center; padding: 24px;">
        <div style="font-size: 36px; margin-bottom: 12px;">⏳</div>
        <h4 style="font-size: 18px; font-weight: 700; margin-bottom: 8px;">Order Pending Synchronization</h4>
        <p style="color: var(--text-muted); max-width: 500px; margin: 0 auto 16px; font-size: 14px;">
          Tracking ID <strong>${escapeHtml(trackingId)}</strong> is not yet indexed in the Forward Scan database.
          Orders typically sync every 3–5 minutes from the marketplace.
        </p>
        <button class="btn btn-secondary btn-sm" onclick="document.getElementById('search-form').dispatchEvent(new Event('submit'))">
          ↻ Retry Check Now
        </button>
      </div>
    `;
    resultsArea.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function renderError(title, detail) {
    resultError.style.display = 'block';
    resultSuccess.style.display = 'none';
    resultError.innerHTML = `
      <div style="text-align: center; padding: 24px;">
        <div style="font-size: 36px; margin-bottom: 12px; color: var(--status-error);">⚠</div>
        <h4 style="font-size: 18px; font-weight: 700; margin-bottom: 8px;">${escapeHtml(title)}</h4>
        <p style="color: var(--text-muted); font-size: 14px;">${escapeHtml(detail)}</p>
      </div>
    `;
    resultsArea.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }
});
