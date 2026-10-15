(() => {
const u=window.vehicleDriverUI;if(!u)return;
Promise.all([u.loadVehicles(),u.loadDrivers(),u.loadCampuses()])
 .then(()=>u.loadLinks()).catch(error=>u.notify(error.message));
})();
