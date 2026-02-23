(function () {
    'use strict';

    // UNCW Colors
    const TEAL = '#007680';
    const GOLD = '#FFD600';
    const NAVY = '#003366';

    // Wilmington, NC center
    const DEFAULT_CENTER = [34.2257, -77.9447];
    const DEFAULT_ZOOM = 12;

    let map;
    let markers = [];
    let allData = null;
    let selectedDateIndex = 0;
    let availableDates = [];
    let userLocation = null;
    let userLocationMarker = null;
    let radiusCircle = null;
    let radiusMiles = 5;

    // Initialize
    document.addEventListener('DOMContentLoaded', init);

    async function init() {
        initMap();
        await loadData();
        setupControls();
        setupMyLocation();
        hideLoading();
    }

    function initMap() {
        map = L.map('map', {
            center: DEFAULT_CENTER,
            zoom: DEFAULT_ZOOM,
            zoomControl: true
        });

        // Dark themed map tiles (CartoDB Dark Matter)
        L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>',
            subdomains: 'abcd',
            maxZoom: 19
        }).addTo(map);
    }

    async function loadData() {
        try {
            const response = await fetch('/api/trucks');
            if (!response.ok) throw new Error('Failed to load data');
            allData = await response.json();
            processData();
        } catch (err) {
            console.error('Failed to load truck data:', err);
            document.getElementById('truckCount').innerHTML =
                '<span style="color: #f85149;">Failed to load data. Run the scraper first.</span>';
        }
    }

    function processData() {
        if (!allData || !allData.allAppearances) return;

        // Collect all unique dates and sort them
        const dateSet = new Set();
        allData.allAppearances.forEach(a => dateSet.add(a.date));
        availableDates = Array.from(dateSet).sort();

        // Find today or the closest future date
        const today = new Date().toISOString().slice(0, 10);
        selectedDateIndex = availableDates.findIndex(d => d >= today);
        if (selectedDateIndex < 0) selectedDateIndex = availableDates.length - 1;

        buildDayPills();
        showDate(selectedDateIndex);
    }

    function buildDayPills() {
        const container = document.getElementById('dayPills');
        container.innerHTML = '';

        availableDates.forEach((date, index) => {
            const btn = document.createElement('button');
            btn.className = 'day-pill';
            const d = parseDate(date);
            const dayName = d.toLocaleDateString('en-US', { weekday: 'short' });
            const monthDay = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });

            // Count trucks for this date
            const count = allData.allAppearances.filter(a => a.date === date).length;

            btn.innerHTML = `${dayName} ${monthDay}`;
            if (count > 0) {
                btn.innerHTML += ` <span class="pill-count">${count}</span>`;
                btn.classList.add('has-trucks');
            }

            btn.addEventListener('click', () => showDate(index));
            container.appendChild(btn);
        });
    }

    function showDate(index) {
        if (index < 0 || index >= availableDates.length) return;
        selectedDateIndex = index;
        const date = availableDates[index];

        // Update active pill
        document.querySelectorAll('.day-pill').forEach((pill, i) => {
            pill.classList.toggle('active', i === index);
        });

        // Update header display
        const d = parseDate(date);
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        const dateObj = new Date(d);
        dateObj.setHours(0, 0, 0, 0);

        const diffDays = Math.round((dateObj - today) / (1000 * 60 * 60 * 24));
        let dayLabel;
        if (diffDays === 0) dayLabel = 'Today';
        else if (diffDays === 1) dayLabel = 'Tomorrow';
        else if (diffDays === -1) dayLabel = 'Yesterday';
        else dayLabel = d.toLocaleDateString('en-US', { weekday: 'long' });

        document.getElementById('currentDay').textContent = dayLabel;
        document.getElementById('currentDate').textContent =
            d.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric' });
        document.getElementById('sidebarDate').textContent =
            d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });

        // Filter appearances for this date
        const allForDate = allData.allAppearances.filter(a => a.date === date);
        const appearances = filterByRadius(allForDate);

        if (userLocation && appearances.length !== allForDate.length) {
            document.getElementById('truckCount').innerHTML =
                `<strong>${appearances.length}</strong> of ${allForDate.length} truck${allForDate.length !== 1 ? 's' : ''} within ${radiusMiles} mi`;
        } else {
            document.getElementById('truckCount').innerHTML =
                `<strong>${appearances.length}</strong> truck${appearances.length !== 1 ? 's' : ''} rolling`;
        }

        updateMarkers(appearances);
        updateSidebar(appearances);
    }

    function updateMarkers(appearances) {
        // Clear existing markers
        markers.forEach(m => map.removeLayer(m));
        markers = [];

        // Group appearances by location (lat/lng) for stacking
        const groups = {};
        appearances.forEach(a => {
            if (!a.latitude || !a.longitude) return;
            const key = `${a.latitude.toFixed(5)},${a.longitude.toFixed(5)}`;
            if (!groups[key]) groups[key] = [];
            groups[key].push(a);
        });

        const bounds = [];

        Object.values(groups).forEach(group => {
            const first = group[0];
            const lat = first.latitude;
            const lng = first.longitude;
            bounds.push([lat, lng]);

            let marker;
            if (group.length > 1) {
                // Cluster icon for multiple trucks at same location
                const icon = L.divIcon({
                    html: `<div class="truck-cluster">${group.length}</div>`,
                    className: 'truck-marker',
                    iconSize: [44, 44],
                    iconAnchor: [22, 22]
                });
                marker = L.marker([lat, lng], { icon }).addTo(map);
            } else {
                // Single truck icon
                const icon = L.divIcon({
                    html: `<div class="truck-marker-icon"><span>🚚</span></div>`,
                    className: 'truck-marker',
                    iconSize: [36, 44],
                    iconAnchor: [18, 44]
                });
                marker = L.marker([lat, lng], { icon }).addTo(map);
            }

            // Build popup content
            let popupHtml = '';
            group.forEach(a => {
                const nameHtml = a.facebookUrl
                    ? `<a href="${a.facebookUrl}" target="_blank" rel="noopener">${escapeHtml(a.truckName)}</a>`
                    : escapeHtml(a.truckName);

                popupHtml += `
                    <div class="popup-multi-truck">
                        <div class="popup-truck-name">${nameHtml}</div>
                        <div class="popup-location">${escapeHtml(a.locationName)}</div>
                        ${a.address ? `<div class="popup-address">${escapeHtml(a.address)}</div>` : ''}
                        <div class="popup-time">${escapeHtml(a.startTime)} – ${escapeHtml(a.endTime)}</div>
                        ${a.description ? `<div class="popup-description">${escapeHtml(a.description)}</div>` : ''}
                    </div>`;
            });

            marker.bindPopup(popupHtml, { maxWidth: 300 });
            markers.push(marker);
        });

        // Fit map to markers if we have any
        if (bounds.length > 0) {
            map.fitBounds(bounds, { padding: [50, 50], maxZoom: 14 });
        }
    }

    function updateSidebar(appearances) {
        const list = document.getElementById('truckList');

        if (appearances.length === 0) {
            list.innerHTML = `
                <div class="no-trucks">
                    <h3>No trucks scheduled</h3>
                    <p>Check another day using the pills above or the arrow buttons.</p>
                </div>`;
            return;
        }

        // Sort by start time
        const sorted = [...appearances].sort((a, b) => {
            const tA = parseTime(a.startTime);
            const tB = parseTime(b.startTime);
            return tA - tB;
        });

        list.innerHTML = sorted.map(a => {
            const nameHtml = a.facebookUrl
                ? `<a href="${a.facebookUrl}" target="_blank" rel="noopener">${escapeHtml(a.truckName)}</a>`
                : escapeHtml(a.truckName);

            return `
                <div class="truck-card" data-lat="${a.latitude}" data-lng="${a.longitude}">
                    <div class="truck-card-name">${nameHtml}</div>
                    <div class="truck-card-location">${escapeHtml(a.locationName)}</div>
                    <div class="truck-card-time">${escapeHtml(a.startTime)} – ${escapeHtml(a.endTime)}</div>
                    <div class="truck-card-desc">${escapeHtml(a.description || '')}</div>
                </div>`;
        }).join('');

        // Click on card -> fly to marker
        list.querySelectorAll('.truck-card').forEach(card => {
            card.addEventListener('click', () => {
                const lat = parseFloat(card.dataset.lat);
                const lng = parseFloat(card.dataset.lng);
                if (!isNaN(lat) && !isNaN(lng)) {
                    map.flyTo([lat, lng], 16, { duration: 0.8 });
                    // Open the popup at that location
                    markers.forEach(m => {
                        const pos = m.getLatLng();
                        if (Math.abs(pos.lat - lat) < 0.0001 && Math.abs(pos.lng - lng) < 0.0001) {
                            m.openPopup();
                        }
                    });
                }
            });
        });
    }

    function setupControls() {
        // Arrow buttons
        document.getElementById('prevDay').addEventListener('click', () => {
            if (selectedDateIndex > 0) showDate(selectedDateIndex - 1);
        });
        document.getElementById('nextDay').addEventListener('click', () => {
            if (selectedDateIndex < availableDates.length - 1) showDate(selectedDateIndex + 1);
        });

        // Sidebar toggle
        const sidebar = document.getElementById('sidebar');
        document.getElementById('listToggle').addEventListener('click', () => {
            sidebar.classList.toggle('open');
        });
        document.getElementById('closeSidebar').addEventListener('click', () => {
            sidebar.classList.remove('open');
        });

        // Keyboard navigation
        document.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowLeft') {
                if (selectedDateIndex > 0) showDate(selectedDateIndex - 1);
            } else if (e.key === 'ArrowRight') {
                if (selectedDateIndex < availableDates.length - 1) showDate(selectedDateIndex + 1);
            } else if (e.key === 'Escape') {
                sidebar.classList.remove('open');
            }
        });
    }

    function hideLoading() {
        document.getElementById('loading').classList.add('hidden');
    }

    // Utilities
    function parseDate(dateStr) {
        // dateStr is "yyyy-MM-dd"
        const [y, m, d] = dateStr.split('-').map(Number);
        return new Date(y, m - 1, d);
    }

    function parseTime(timeStr) {
        if (!timeStr) return 0;
        const match = timeStr.match(/(\d{1,2}):(\d{2})\s*(AM|PM)/i);
        if (!match) return 0;
        let hours = parseInt(match[1]);
        const mins = parseInt(match[2]);
        const ampm = match[3].toUpperCase();
        if (ampm === 'PM' && hours !== 12) hours += 12;
        if (ampm === 'AM' && hours === 12) hours = 0;
        return hours * 60 + mins;
    }

    function setupMyLocation() {
        const btn = document.getElementById('myLocation');
        const radiusControl = document.getElementById('radiusControl');
        const radiusSlider = document.getElementById('radiusSlider');
        const radiusValueEl = document.getElementById('radiusValue');
        const clearBtn = document.getElementById('clearRadius');

        btn.addEventListener('click', () => {
            if (userLocation) {
                // Toggle off
                clearUserLocation();
                return;
            }

            if (!navigator.geolocation) {
                alert('Geolocation is not supported by your browser.');
                return;
            }

            btn.classList.add('active');
            navigator.geolocation.getCurrentPosition(
                (position) => {
                    userLocation = {
                        lat: position.coords.latitude,
                        lng: position.coords.longitude
                    };

                    // Add user location marker
                    const icon = L.divIcon({
                        html: '<div class="user-location-dot"></div>',
                        className: '',
                        iconSize: [16, 16],
                        iconAnchor: [8, 8]
                    });
                    userLocationMarker = L.marker([userLocation.lat, userLocation.lng], { icon, zIndexOffset: 1000 }).addTo(map);

                    // Show radius control
                    radiusControl.classList.remove('hidden');
                    drawRadiusCircle();

                    // Re-render current day with filter
                    showDate(selectedDateIndex);
                    map.flyTo([userLocation.lat, userLocation.lng], 13, { duration: 0.8 });
                },
                (err) => {
                    btn.classList.remove('active');
                    alert('Could not get your location. Please allow location access.');
                    console.error('Geolocation error:', err);
                },
                { enableHighAccuracy: true, timeout: 10000 }
            );
        });

        radiusSlider.addEventListener('input', () => {
            radiusMiles = parseInt(radiusSlider.value);
            radiusValueEl.textContent = radiusMiles;
            drawRadiusCircle();
            showDate(selectedDateIndex);
        });

        clearBtn.addEventListener('click', () => {
            clearUserLocation();
        });
    }

    function clearUserLocation() {
        const btn = document.getElementById('myLocation');
        const radiusControl = document.getElementById('radiusControl');

        if (userLocationMarker) {
            map.removeLayer(userLocationMarker);
            userLocationMarker = null;
        }
        if (radiusCircle) {
            map.removeLayer(radiusCircle);
            radiusCircle = null;
        }
        userLocation = null;
        btn.classList.remove('active');
        radiusControl.classList.add('hidden');
        showDate(selectedDateIndex);
    }

    function drawRadiusCircle() {
        if (radiusCircle) map.removeLayer(radiusCircle);
        if (!userLocation) return;

        const radiusMeters = radiusMiles * 1609.34;
        radiusCircle = L.circle([userLocation.lat, userLocation.lng], {
            radius: radiusMeters,
            color: '#007680',
            fillColor: '#007680',
            fillOpacity: 0.08,
            weight: 2,
            dashArray: '6, 4'
        }).addTo(map);
    }

    function distanceMiles(lat1, lng1, lat2, lng2) {
        const R = 3958.8; // Earth radius in miles
        const dLat = (lat2 - lat1) * Math.PI / 180;
        const dLng = (lng2 - lng1) * Math.PI / 180;
        const a = Math.sin(dLat / 2) ** 2 +
                  Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
                  Math.sin(dLng / 2) ** 2;
        return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    function filterByRadius(appearances) {
        if (!userLocation) return appearances;
        return appearances.filter(a => {
            if (!a.latitude || !a.longitude) return false;
            return distanceMiles(userLocation.lat, userLocation.lng, a.latitude, a.longitude) <= radiusMiles;
        });
    }

    function escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
})();
