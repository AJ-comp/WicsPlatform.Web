// 화면이 뜨기 전에 반드시 실행될 PreProcess 함수
function PreProcess() {
    // 확인 대화상자 표시 - 이것이 페이지 로딩 전에 실행되는지 확인
//    alert("PreProcess 함수가 실행되었습니다!");

    // 콘솔에도 로그 남기기
//    console.log("PreProcess 함수가 실행되었습니다!");

    // 여기에 필요한 초기화 코드를 넣으세요
    // localStorage 확인, API 호출 등 어떤 작업이든 가능합니다

    // 작업 완료 표시
    window.preprocessCompleted = true;
}

// 페이지 로드 시 바로 실행
PreProcess();
